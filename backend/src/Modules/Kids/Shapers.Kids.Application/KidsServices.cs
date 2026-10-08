using Microsoft.EntityFrameworkCore;
using Shapers.Church.Contracts;
using Shapers.Kids.Contracts;
using Shapers.Kids.Domain;
using Shapers.People.Contracts;
using Shapers.Platform.Auditing;
using Shapers.Platform.Authorization;
using Shapers.Platform.Calendar;

namespace Shapers.Kids.Application;

/// <summary>The classes a child could go to, and picking one by age.</summary>
public sealed class ClassFinder(IKidsDb db)
{
    public async Task<IReadOnlyList<KidsClass>> ActiveAsync(CancellationToken cancellationToken) =>
        await db.Classes.AsNoTracking().Where(c => !c.IsArchived).OrderBy(c => c.FromAge).ToListAsync(cancellationToken);

    /// <summary>The narrowest class for the child's age among classes set up for their campus or the whole church.</summary>
    public static KidsClass? For(IReadOnlyList<KidsClass> classes, ChildSummary child, DateOnly date) =>
        child.DateOfBirth is { } dob
            ? KidsClass.For(classes.Where(c => ScopePath.Parse(c.Scope).Covers(ScopePath.Parse(child.Scope))), KidsClass.AgeOn(dob, date))
            : null;
}

/// <summary>Shared by the parent and desk check-ins: one pickup code for the children checked in together.</summary>
public sealed class CheckInWriter(IKidsDb db, ClassFinder finder, TimeProvider clock)
{
    private const int CodeAttempts = 20;

    public async Task<Result<IReadOnlyList<CheckIn>>> CheckInAsync(
        IReadOnlyList<ChildSummary> children, Guid? guardianId, CheckInMethod method, Guid? byUserId, CancellationToken cancellationToken)
    {
        if (children.Count == 0)
        {
            return new Error("kids.no_children", "Choose at least one child to check in.");
        }

        var now = clock.GetUtcNow();
        var today = ChurchTime.DateOf(now);
        var ids = children.Select(c => c.PersonId).ToList();
        var already = await db.CheckIns.AsNoTracking()
            .Where(c => ids.Contains(c.ChildId) && c.Date == today && c.CollectedAt == null)
            .Select(c => c.ChildId)
            .ToListAsync(cancellationToken);
        if (already.Count > 0)
        {
            var name = children.First(c => already.Contains(c.PersonId)).FirstName;
            return Error.Conflict("kids.already_checked_in", $"{name} is already checked in today.");
        }

        var classes = await finder.ActiveAsync(cancellationToken);
        var placed = new List<(ChildSummary Child, KidsClass Class)>();
        foreach (var child in children)
        {
            if (child.DateOfBirth is null)
            {
                return new Error("kids.dob_required", $"Add {child.FirstName}'s date of birth so we know which class they go to.");
            }

            if (ClassFinder.For(classes, child, today) is not { } kidsClass)
            {
                return new Error("kids.no_class", $"There's no kids class for {child.FirstName}'s age yet. Please speak to the kids team.");
            }

            placed.Add((child, kidsClass));
        }

        var code = await NewCodeAsync(today, cancellationToken);
        var checkIns = placed
            .Select(p => CheckIn.Create(p.Child.PersonId, guardianId, p.Class, ScopePath.Parse(p.Child.Scope), today, code, method, byUserId, now))
            .ToList();
        db.CheckIns.AddRange(checkIns);
        await db.SaveChangesAsync(cancellationToken);
        return checkIns;
    }

    /// <summary>A code nobody else has open today, so a code only ever matches one family's children.</summary>
    private async Task<string> NewCodeAsync(DateOnly today, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < CodeAttempts; attempt++)
        {
            var code = CheckIn.NewCode();
            if (!await db.CheckIns.AnyAsync(c => c.Date == today && c.PickupCode == code && c.CollectedAt == null, cancellationToken))
            {
                return code;
            }
        }

        throw new InvalidOperationException("Could not find an unused pickup code.");
    }
}

/// <summary>Parents in the app: their children, adding a child, care notes and checking in.</summary>
public sealed class ParentKidsService(IKidsDb db, IFamilyRecords families, ClassFinder finder, CheckInWriter writer, ICurrentUser currentUser, TimeProvider clock)
{
    private static readonly Error SignIn = Error.Unauthorized("kids.sign_in", "Sign in to check your children in.");
    private static readonly Error NotYourChild = Error.NotFound("kids.child_not_found", "Child not found in your household.");

    public async Task<Result<IReadOnlyList<MyChildDto>>> MyChildrenAsync(CancellationToken cancellationToken)
    {
        if (currentUser.PersonId is not { } me)
        {
            return SignIn;
        }

        var children = await families.ChildrenOfAsync(me, cancellationToken);
        var ids = children.Select(c => c.PersonId).ToList();
        var today = ChurchTime.DateOf(clock.GetUtcNow());
        var notes = await db.CareNotes.AsNoTracking().Where(n => ids.Contains(n.ChildId)).ToDictionaryAsync(n => n.ChildId, cancellationToken);
        var checkIns = (await db.CheckIns.AsNoTracking().Where(c => ids.Contains(c.ChildId) && c.Date == today).ToListAsync(cancellationToken))
            .GroupBy(c => c.ChildId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.CheckedInAt).First());
        var classes = await finder.ActiveAsync(cancellationToken);

        return children.Select(c =>
            {
                var note = notes.GetValueOrDefault(c.PersonId);
                var checkIn = checkIns.GetValueOrDefault(c.PersonId);
                return new MyChildDto(
                    c.PersonId,
                    c.FirstName,
                    c.DisplayName,
                    c.DateOfBirth,
                    c.DateOfBirth is { } dob ? KidsClass.AgeOn(dob, today) : null,
                    ClassFinder.For(classes, c, today)?.Name,
                    note is null || note.IsEmpty ? null : new CareNotesDto(note.Allergies, note.Medical, note.Other),
                    checkIn is null ? null : new MyChildCheckInDto(checkIn.Id, checkIn.ClassName, checkIn.PickupCode, checkIn.CheckedInAt, checkIn.CollectedAt));
            })
            .ToList();
    }

    public async Task<Result<IReadOnlyList<MyChildDto>>> AddChildAsync(AddChildRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.PersonId is not { } me)
        {
            return SignIn;
        }

        if (!request.GuardianConsent)
        {
            return new Error("kids.consent_required", "Please confirm you're this child's parent or guardian and agree to the church keeping these details.");
        }

        var added = await families.AddChildrenAsync(me, [new ChildDetails(request.FirstName, request.LastName, request.DateOfBirth)], recordedByUserId: null, cancellationToken);
        if (added.IsFailure)
        {
            return added.Error!;
        }

        await SaveNotesAsync(added.Value[0], request.Allergies, request.Medical, request.Other, cancellationToken);
        return await MyChildrenAsync(cancellationToken);
    }

    public async Task<Result<IReadOnlyList<MyChildDto>>> UpdateCareNotesAsync(Guid childId, CareNotesRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.PersonId is not { } me)
        {
            return SignIn;
        }

        if (!(await families.ChildrenOfAsync(me, cancellationToken)).Any(c => c.PersonId == childId))
        {
            return NotYourChild;
        }

        await SaveNotesAsync(childId, request.Allergies, request.Medical, request.Other, cancellationToken);
        return await MyChildrenAsync(cancellationToken);
    }

    public async Task<Result<IReadOnlyList<MyChildDto>>> CheckInAsync(ParentCheckInRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.PersonId is not { } me)
        {
            return SignIn;
        }

        var mine = await families.ChildrenOfAsync(me, cancellationToken);
        var chosen = mine.Where(c => request.ChildIds.Contains(c.PersonId)).ToList();
        if (chosen.Count != request.ChildIds.Distinct().Count())
        {
            return NotYourChild;
        }

        var result = await writer.CheckInAsync(chosen, me, CheckInMethod.App, byUserId: null, cancellationToken);
        return result.IsFailure ? result.Error! : await MyChildrenAsync(cancellationToken);
    }

    private async Task SaveNotesAsync(Guid childId, string? allergies, string? medical, string? other, CancellationToken cancellationToken)
    {
        var note = await db.CareNotes.SingleOrDefaultAsync(n => n.ChildId == childId, cancellationToken);
        if (note is null)
        {
            note = CareNote.For(childId);
            db.CareNotes.Add(note);
        }

        note.Update(allergies, medical, other, clock.GetUtcNow());
        if (note.IsEmpty)
        {
            db.CareNotes.Remove(note);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>The kids team: today's classes, the desk for visiting families, labels and handing children back.</summary>
public sealed class KidsDeskService(
    IKidsDb db,
    IFamilyRecords families,
    IGuestRecords guests,
    IChurchDirectory church,
    ClassFinder finder,
    CheckInWriter writer,
    IAuthorizer authorizer,
    ICurrentUser currentUser,
    IAuditLog audit,
    TimeProvider clock)
{
    private static readonly Error NoMatch = Error.NotFound("kids.code_not_found", "No child is waiting to be collected with that code. Check it with the parent, or ask a leader.");

    public async Task<KidsTodayDto> TodayAsync(CancellationToken cancellationToken)
    {
        var scopes = await authorizer.ScopesForAsync(KidsPermissions.CheckIn, cancellationToken);
        var today = ChurchTime.DateOf(clock.GetUtcNow());
        var checkIns = await db.CheckIns.AsNoTracking().Where(c => c.Date == today).WithinScopes(c => c.Scope, scopes).OrderBy(c => c.CheckedInAt).ToListAsync(cancellationToken);
        var childIds = checkIns.Select(c => c.ChildId).Distinct().ToList();
        var children = await families.GetChildrenAsync(childIds, cancellationToken);
        var withNotes = (await db.CareNotes.AsNoTracking().Where(n => childIds.Contains(n.ChildId)).Select(n => n.ChildId).ToListAsync(cancellationToken)).ToHashSet();
        var classes = (await finder.ActiveAsync(cancellationToken))
            .Where(c => scopes.Any(s => ScopePath.Parse(c.Scope).Covers(s) || s.Covers(ScopePath.Parse(c.Scope))))
            .ToList();

        var byClass = checkIns.GroupBy(c => c.ClassId).ToDictionary(g => g.Key, g => g.ToList());
        var shown = classes.Select(c => (c.Id, c.Name, c.FromAge, c.ToAge)).ToList();
        // A class archived or out of scope since this morning still shows the children checked into it.
        shown.AddRange(byClass.Keys.Where(id => classes.All(c => c.Id != id)).Select(id => (id, byClass[id][0].ClassName, 0, 0)));

        return new KidsTodayDto(today, shown.Select(c => new ClassTodayDto(
                c.Item1,
                c.Item2,
                c.Item3,
                c.Item4,
                byClass.GetValueOrDefault(c.Item1, []).Select(ci =>
                {
                    var child = children.GetValueOrDefault(ci.ChildId);
                    return new CheckedInChildDto(
                        ci.Id,
                        ci.ChildId,
                        child?.DisplayName ?? "Child",
                        child?.DateOfBirth is { } dob ? KidsClass.AgeOn(dob, today) : null,
                        withNotes.Contains(ci.ChildId),
                        ci.CheckedInAt,
                        ci.Method,
                        ci.CollectedAt);
                }).ToList()))
            .ToList());
    }

    /// <summary>The children waiting to be collected with this code. A wrong code reveals nothing.</summary>
    public async Task<Result<PickupDto>> FindByCodeAsync(string code, CancellationToken cancellationToken)
    {
        var normalised = CheckIn.Normalise(code);
        if (!CheckIn.IsValidCode(normalised))
        {
            return NoMatch;
        }

        var scopes = await authorizer.ScopesForAsync(KidsPermissions.CheckIn, cancellationToken);
        var today = ChurchTime.DateOf(clock.GetUtcNow());
        var waiting = await db.CheckIns.AsNoTracking()
            .Where(c => c.Date == today && c.PickupCode == normalised && c.CollectedAt == null)
            .WithinScopes(c => c.Scope, scopes)
            .ToListAsync(cancellationToken);
        if (waiting.Count == 0)
        {
            return NoMatch;
        }

        var children = await families.GetChildrenAsync(waiting.Select(c => c.ChildId).ToList(), cancellationToken);
        return new PickupDto(normalised, waiting.Select(c => new PickupChildDto(c.Id, children.GetValueOrDefault(c.ChildId)?.DisplayName ?? "Child", c.ClassName, c.CheckedInAt)).ToList());
    }

    /// <summary>Hands children back to the person showing the code. Each check-in is checked against the code again.</summary>
    public async Task<Result<PickupDto>> CheckOutAsync(CheckOutRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } by)
        {
            return Error.Unauthorized("kids.sign_in", "Sign in to hand children back.");
        }

        var ids = request.CheckInIds.Distinct().ToList();
        var checkIns = await db.CheckIns.Where(c => ids.Contains(c.Id)).ToListAsync(cancellationToken);
        if (ids.Count == 0 || checkIns.Count != ids.Count)
        {
            return NoMatch;
        }

        var now = clock.GetUtcNow();
        foreach (var checkIn in checkIns)
        {
            if (!await authorizer.CanAsync(KidsPermissions.CheckIn, ScopePath.Parse(checkIn.Scope), cancellationToken))
            {
                return NoMatch;
            }

            checkIn.Collect(request.Code, by, now);
        }

        await db.SaveChangesAsync(cancellationToken);
        foreach (var checkIn in checkIns)
        {
            await audit.RecordAsync(new AuditRecord("kids.child.collected", "kids_check_in", checkIn.Id.ToString(), ScopePath.Parse(checkIn.Scope)), cancellationToken);
        }

        var children = await families.GetChildrenAsync(checkIns.Select(c => c.ChildId).ToList(), cancellationToken);
        return new PickupDto(CheckIn.Normalise(request.Code), checkIns.Select(c => new PickupChildDto(c.Id, children.GetValueOrDefault(c.ChildId)?.DisplayName ?? "Child", c.ClassName, c.CheckedInAt)).ToList());
    }

    /// <summary>A visiting family without the app: the parent's record, each child in their household, care notes, and the check-in.</summary>
    public async Task<Result<DeskCheckInResultDto>> DeskCheckInAsync(DeskCheckInRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } by)
        {
            return Error.Unauthorized("kids.sign_in", "Sign in to check children in.");
        }

        if (request.Children.Count == 0)
        {
            return new Error("kids.no_children", "Add at least one child.");
        }

        var campuses = await church.GetCampusesAsync(cancellationToken);
        var campusScope = ScopePath.Parse((campuses.FirstOrDefault(c => c.IsPrimary) ?? campuses[0]).Scope);
        if (!await authorizer.CanAsync(KidsPermissions.CheckIn, campusScope, cancellationToken))
        {
            return Error.Forbidden("kids.forbidden", "You can't check children in at this campus.");
        }

        if (!request.Consent)
        {
            return new Error("kids.consent_required", "Ask the parent if the church may keep their and their children's details, and tick the box when they agree.");
        }

        var parent = await guests.CreateAsync(
            new GuestDetails(request.ParentFirstName, request.ParentLastName, request.ParentMobile, null, false, true, GuestOrigin.KidsCheckIn, FromWebsite: false, PolicyVersion: null),
            cancellationToken);
        if (parent.IsFailure)
        {
            return parent.Error!;
        }

        var added = await families.AddChildrenAsync(parent.Value, [.. request.Children.Select(c => new ChildDetails(c.FirstName, c.LastName, c.DateOfBirth))], by, cancellationToken);
        if (added.IsFailure)
        {
            return added.Error!;
        }

        var childIds = added.Value;
        foreach (var (child, childId) in request.Children.Zip(childIds))
        {
            if (!string.IsNullOrWhiteSpace(child.Allergies) || !string.IsNullOrWhiteSpace(child.Medical) || !string.IsNullOrWhiteSpace(child.Other))
            {
                var note = CareNote.For(childId);
                note.Update(child.Allergies, child.Medical, child.Other, clock.GetUtcNow());
                db.CareNotes.Add(note);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        var summaries = await families.GetChildrenAsync(childIds, cancellationToken);
        var checkedIn = await writer.CheckInAsync([.. childIds.Select(id => summaries[id])], parent.Value, CheckInMethod.Desk, by, cancellationToken);
        if (checkedIn.IsFailure)
        {
            return checkedIn.Error!;
        }

        foreach (var checkIn in checkedIn.Value)
        {
            await audit.RecordAsync(new AuditRecord("kids.child.checked_in_at_desk", "kids_check_in", checkIn.Id.ToString(), ScopePath.Parse(checkIn.Scope)), cancellationToken);
        }

        return new DeskCheckInResultDto(checkedIn.Value[0].PickupCode, await LabelsAsync(checkedIn.Value, cancellationToken));
    }

    /// <summary>A name label to print: shows the pickup code, so it's recorded who printed it.</summary>
    public async Task<Result<KidsLabelDto>> LabelAsync(Guid checkInId, CancellationToken cancellationToken)
    {
        var checkIn = await db.CheckIns.AsNoTracking().SingleOrDefaultAsync(c => c.Id == checkInId, cancellationToken);
        if (checkIn is null || !await authorizer.CanAsync(KidsPermissions.CheckIn, ScopePath.Parse(checkIn.Scope), cancellationToken))
        {
            return Error.NotFound("kids.check_in_not_found", "Check-in not found.");
        }

        await audit.RecordAsync(new AuditRecord("kids.label.printed", "kids_check_in", checkIn.Id.ToString(), ScopePath.Parse(checkIn.Scope)), cancellationToken);
        return (await LabelsAsync([checkIn], cancellationToken))[0];
    }

    /// <summary>A child's care notes, for leaders with the care permission in a second-factor session. Every read is audited.</summary>
    public async Task<Result<CareNotesDto>> CareNotesAsync(Guid childId, CancellationToken cancellationToken)
    {
        var child = (await families.GetChildrenAsync([childId], cancellationToken)).GetValueOrDefault(childId);
        if (child is null || !await authorizer.CanAsync(KidsPermissions.CareView, ScopePath.Parse(child.Scope), cancellationToken))
        {
            return Error.NotFound("kids.child_not_found", "Child not found.");
        }

        var note = await db.CareNotes.AsNoTracking().SingleOrDefaultAsync(n => n.ChildId == childId, cancellationToken);
        await audit.RecordAsync(new AuditRecord("kids.care_notes.viewed", "child", childId.ToString(), ScopePath.Parse(child.Scope), IsSensitiveRead: true), cancellationToken);
        return note is null ? new CareNotesDto(null, null, null) : new CareNotesDto(note.Allergies, note.Medical, note.Other);
    }

    private async Task<IReadOnlyList<KidsLabelDto>> LabelsAsync(IReadOnlyList<CheckIn> checkIns, CancellationToken cancellationToken)
    {
        var ids = checkIns.Select(c => c.ChildId).ToList();
        var children = await families.GetChildrenAsync(ids, cancellationToken);
        var withNotes = (await db.CareNotes.AsNoTracking().Where(n => ids.Contains(n.ChildId)).Select(n => n.ChildId).ToListAsync(cancellationToken)).ToHashSet();
        return checkIns.Select(c => new KidsLabelDto(c.Id, children.GetValueOrDefault(c.ChildId)?.DisplayName ?? "Child", c.ClassName, c.Date, c.PickupCode, withNotes.Contains(c.ChildId))).ToList();
    }
}

/// <summary>Setting up the classes. The four defaults are created once, for the church to rename or change.</summary>
public sealed class KidsClassService(IKidsDb db, IChurchDirectory church, IAuthorizer authorizer, IAuditLog audit, TimeProvider clock)
{
    private static readonly Error NotFound = Error.NotFound("kids.class_not_found", "Class not found.");

    public static readonly (string Name, int From, int To)[] Defaults = [("Little ones", 0, 2), ("Pre-school", 3, 5), ("Primary", 6, 9), ("Pre-teens", 10, 12)];

    public async Task<IReadOnlyList<KidsClassDto>> ListAsync(CancellationToken cancellationToken) =>
        await db.Classes.AsNoTracking().OrderBy(c => c.IsArchived).ThenBy(c => c.FromAge).ThenBy(c => c.Name)
            .Select(c => new KidsClassDto(c.Id, c.Name, c.FromAge, c.ToAge, c.IsArchived))
            .ToListAsync(cancellationToken);

    public async Task<Result<KidsClassDto>> CreateAsync(SaveKidsClassRequest request, CancellationToken cancellationToken)
    {
        var root = ScopePath.Parse((await church.GetRootScopeAsync(cancellationToken)).Path);
        if (!await authorizer.CanAsync(KidsPermissions.Manage, root, cancellationToken))
        {
            return Error.Forbidden("kids.forbidden", "You can't set up kids classes.");
        }

        var kidsClass = KidsClass.Create(request.Name, request.FromAge, request.ToAge, root, clock.GetUtcNow());
        db.Classes.Add(kidsClass);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord("kids.class.created", "kids_class", kidsClass.Id.ToString(), root), cancellationToken);
        return ToDto(kidsClass);
    }

    public Task<Result<KidsClassDto>> UpdateAsync(Guid id, SaveKidsClassRequest request, CancellationToken cancellationToken) =>
        EditAsync(id, "kids.class.updated", c => c.Update(request.Name, request.FromAge, request.ToAge), cancellationToken);

    public Task<Result<KidsClassDto>> ArchiveAsync(Guid id, CancellationToken cancellationToken) =>
        EditAsync(id, "kids.class.archived", c => c.Archive(), cancellationToken);

    public Task<Result<KidsClassDto>> RestoreAsync(Guid id, CancellationToken cancellationToken) =>
        EditAsync(id, "kids.class.restored", c => c.Restore(), cancellationToken);

    /// <summary>On first start: the default classes, so check-in works before the church has set its own.</summary>
    public async Task SeedDefaultsAsync(CancellationToken cancellationToken)
    {
        if (await db.Classes.AnyAsync(cancellationToken))
        {
            return;
        }

        var root = ScopePath.Parse((await church.GetRootScopeAsync(cancellationToken)).Path);
        var now = clock.GetUtcNow();
        db.Classes.AddRange(Defaults.Select(d => KidsClass.Create(d.Name, d.From, d.To, root, now)));
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<Result<KidsClassDto>> EditAsync(Guid id, string action, Action<KidsClass> change, CancellationToken cancellationToken)
    {
        var kidsClass = await db.Classes.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (kidsClass is null || !await authorizer.CanAsync(KidsPermissions.Manage, ScopePath.Parse(kidsClass.Scope), cancellationToken))
        {
            return NotFound;
        }

        change(kidsClass);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord(action, "kids_class", kidsClass.Id.ToString(), ScopePath.Parse(kidsClass.Scope)), cancellationToken);
        return ToDto(kidsClass);
    }

    private static KidsClassDto ToDto(KidsClass c) => new(c.Id, c.Name, c.FromAge, c.ToAge, c.IsArchived);
}

/// <summary>A child's check-ins and care notes; and for a parent, forgetting them as the guardian on their children's check-ins.</summary>
public sealed class KidsPersonalData(IKidsDb db) : Shapers.Platform.Privacy.IPersonalDataSource
{
    public string Name => "Kids check-in";

    public async Task<object?> ExportAsync(Guid personId, CancellationToken cancellationToken)
    {
        var note = await db.CareNotes.AsNoTracking().SingleOrDefaultAsync(n => n.ChildId == personId, cancellationToken);
        var attended = await db.CheckIns.AsNoTracking().Where(c => c.ChildId == personId).OrderBy(c => c.CheckedInAt)
            .Select(c => new { c.Date, c.ClassName, c.CheckedInAt, c.CollectedAt, Method = c.Method.ToString() })
            .ToListAsync(cancellationToken);
        var checkedInOthers = await db.CheckIns.AsNoTracking().CountAsync(c => c.GuardianId == personId, cancellationToken);
        return note is null && attended.Count == 0 && checkedInOthers == 0
            ? null
            : new { CareNotes = note is null ? null : new { note.Allergies, note.Medical, note.Other, note.UpdatedAt }, CheckIns = attended, ChildrenYouCheckedIn = checkedInOthers };
    }

    public async Task<int> EraseAsync(Guid personId, CancellationToken cancellationToken)
    {
        var notes = await db.CareNotes.Where(n => n.ChildId == personId).ExecuteDeleteAsync(cancellationToken);
        var attended = await db.CheckIns.Where(c => c.ChildId == personId).ExecuteDeleteAsync(cancellationToken);
        var asGuardian = await db.CheckIns.Where(c => c.GuardianId == personId).ExecuteUpdateAsync(s => s.SetProperty(c => c.GuardianId, (Guid?)null), cancellationToken);
        return notes + attended + asGuardian;
    }
}

/// <summary>
/// Nightly: check-ins are deleted two years after the day (long enough to look back if a safeguarding concern is raised),
/// and care notes once the child hasn't been checked in for two years and the notes haven't changed in that time.
/// </summary>
public sealed class KidsRetentionJob(IKidsDb db, TimeProvider clock)
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(730);

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var cutoff = now - Retention;
        var cutoffDate = ChurchTime.DateOf(cutoff);
        var checkIns = await db.CheckIns.Where(c => c.Date < cutoffDate).ExecuteDeleteAsync(cancellationToken);
        var recent = db.CheckIns.Where(c => c.CheckedInAt >= cutoff).Select(c => c.ChildId);
        var notes = await db.CareNotes.Where(n => n.UpdatedAt < cutoff && !recent.Contains(n.ChildId)).ExecuteDeleteAsync(cancellationToken);
        return checkIns + notes;
    }
}
