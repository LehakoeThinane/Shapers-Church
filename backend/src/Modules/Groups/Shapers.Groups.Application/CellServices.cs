using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shapers.Church.Contracts;
using Shapers.Groups.Contracts;
using Shapers.Groups.Domain;
using Shapers.People.Contracts;
using Shapers.Platform.Auditing;
using Shapers.Platform.Authorization;
using Shapers.Platform.Messaging;

namespace Shapers.Groups.Application;

/// <summary>Pastors: set up cells, choose leaders and members, and see how every cell at their campus is doing.</summary>
public sealed class CellAdminService(IGroupsDb db, IChurchDirectory church, IPeopleDirectory people, IAuthorizer authorizer, IAuditLog audit, TimeProvider clock)
{
    private static readonly Error NotFound = Error.NotFound("groups.cell_not_found", "Cell not found.");

    public async Task<IReadOnlyList<CellSummaryDto>> ListAsync(bool includeClosed, CancellationToken cancellationToken)
    {
        var scopes = await authorizer.ScopesForAsync(GroupsPermissions.CellsManage, cancellationToken);
        var reportScopes = await authorizer.ScopesForAsync(GroupsPermissions.ReportsView, cancellationToken);
        var query = db.Cells.AsNoTracking().Include(c => c.Members).WithinScopes(c => c.Scope, scopes.Union(reportScopes).ToList());
        if (!includeClosed)
        {
            query = query.Where(c => c.Status == CellStatus.Active);
        }

        var cells = await query.OrderBy(c => c.Name).ToListAsync(cancellationToken);
        var ids = cells.Select(c => c.Id).ToList();
        var weekAgo = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).AddDays(-6);
        var reports = await db.Reports.AsNoTracking()
            .Where(r => ids.Contains(r.CellId) && r.Status == ReportStatus.Submitted)
            .Select(r => new { r.CellId, r.MeetingDate, r.MembersPresent, r.VisitorCount, r.FollowUps })
            .ToListAsync(cancellationToken);
        var byCell = reports.GroupBy(r => r.CellId).ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.MeetingDate).ToList());
        var leaderIds = cells.SelectMany(c => c.ActiveMembers.Where(m => m.Role != CellRole.Member).Select(m => m.PersonId)).Distinct().ToList();
        var names = await people.GetManyAsync(leaderIds, cancellationToken);

        return cells.Select(c =>
        {
            var cellReports = byCell.GetValueOrDefault(c.Id) ?? [];
            var last = cellReports.FirstOrDefault();
            return new CellSummaryDto(
                c.Id,
                c.Name,
                c.CampusId,
                c.MeetingDay,
                c.MeetingTime,
                c.Area,
                c.Status,
                c.ActiveMembers.Where(m => m.Role != CellRole.Member).OrderBy(m => m.Role).Select(m => names.GetValueOrDefault(m.PersonId)?.DisplayName ?? "Unknown").ToList(),
                c.ActiveMembers.Count(),
                last?.MeetingDate,
                last is null ? null : last.MembersPresent + last.VisitorCount,
                cellReports.Any(r => r.MeetingDate >= weekAgo),
                cellReports.Sum(r => r.FollowUps.Count(f => f.Urgent && f.ResolvedAt is null)));
        }).ToList();
    }

    public async Task<Result<CellDetailDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var cell = await db.Cells.AsNoTracking().Include(c => c.Members).SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (cell is null || !await CanSeeAsync(cell, cancellationToken))
        {
            return NotFound;
        }

        await audit.RecordAsync(new AuditRecord("groups.cell.viewed", "cell", cell.Id.ToString(), ScopePath.Parse(cell.Scope), IsSensitiveRead: true), cancellationToken);
        return await CellDetails.ToDtoAsync(cell, people, cancellationToken);
    }

    public async Task<Result<CellDetailDto>> CreateAsync(SaveCellRequest request, CancellationToken cancellationToken)
    {
        var campus = await church.GetCampusAsync(request.CampusId, cancellationToken);
        if (campus is null || !await authorizer.CanAsync(GroupsPermissions.CellsManage, ScopePath.Parse(campus.Scope), cancellationToken))
        {
            return Error.NotFound("groups.campus_not_found", "Campus not found.");
        }

        var cell = Cell.Create(request.Name, campus.Id, ScopePath.Parse(campus.Scope), request.MeetingDay, request.MeetingTime, request.Area, request.Address, clock.GetUtcNow());
        db.Cells.Add(cell);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord("groups.cell.created", "cell", cell.Id.ToString(), ScopePath.Parse(cell.Scope)), cancellationToken);
        return await CellDetails.ToDtoAsync(cell, people, cancellationToken);
    }

    public Task<Result<CellDetailDto>> UpdateAsync(Guid id, SaveCellRequest request, CancellationToken cancellationToken) =>
        ChangeAsync(id, "groups.cell.updated", c => c.UpdateDetails(request.Name, request.MeetingDay, request.MeetingTime, request.Area, request.Address), cancellationToken);

    public Task<Result<CellDetailDto>> CloseAsync(Guid id, CancellationToken cancellationToken) =>
        ChangeAsync(id, "groups.cell.closed", c => c.Close(clock.GetUtcNow()), cancellationToken);

    public async Task<Result<CellDetailDto>> AddMemberAsync(Guid id, AddMemberRequest request, CancellationToken cancellationToken)
    {
        if (await people.GetAsync(request.PersonId, cancellationToken) is not { MergedIntoId: null } person || person.Status == "Erased")
        {
            return Error.NotFound("groups.person_not_found", "Person not found.");
        }

        return await ChangeAsync(id, "groups.member.added", c => c.AddMember(person.Id, request.Role, clock.GetUtcNow()), cancellationToken);
    }

    public Task<Result<CellDetailDto>> RemoveMemberAsync(Guid id, Guid personId, CancellationToken cancellationToken) =>
        ChangeAsync(id, "groups.member.removed", c => c.RemoveMember(personId, clock.GetUtcNow()), cancellationToken);

    private async Task<Result<CellDetailDto>> ChangeAsync(Guid id, string action, Action<Cell> change, CancellationToken cancellationToken)
    {
        var cell = await db.Cells.Include(c => c.Members).SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (cell is null || !await authorizer.CanAsync(GroupsPermissions.CellsManage, ScopePath.Parse(cell.Scope), cancellationToken))
        {
            return NotFound;
        }

        var before = cell.Members.Select(m => m.Id).ToHashSet();
        change(cell);
        CellDetails.TrackNewMembers(db, cell, before);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord(action, "cell", cell.Id.ToString(), ScopePath.Parse(cell.Scope)), cancellationToken);
        return await CellDetails.ToDtoAsync(cell, people, cancellationToken);
    }

    private async Task<bool> CanSeeAsync(Cell cell, CancellationToken cancellationToken)
    {
        var scope = ScopePath.Parse(cell.Scope);
        return await authorizer.CanAsync(GroupsPermissions.CellsManage, scope, cancellationToken)
            || await authorizer.CanAsync(GroupsPermissions.ReportsView, scope, cancellationToken);
    }
}

/// <summary>Pastors reading reports and materials. Every read is a sensitive read and is audited.</summary>
public sealed class CellReportsService(IGroupsDb db, IPeopleDirectory people, IAuthorizer authorizer, ICurrentUser currentUser, IAuditLog audit, TimeProvider clock)
{
    private static readonly Error NotFound = Error.NotFound("groups.report_not_found", "Report not found.");

    public async Task<IReadOnlyList<ReportSummaryDto>> ListAsync(Guid? cellId, bool urgentOnly, CancellationToken cancellationToken)
    {
        var scopes = await authorizer.ScopesForAsync(GroupsPermissions.ReportsView, cancellationToken);
        var query = db.Reports.AsNoTracking().WithinScopes(r => r.Scope, scopes).Where(r => r.Status == ReportStatus.Submitted);
        if (cellId is { } id)
        {
            query = query.Where(r => r.CellId == id);
        }

        var reports = await query.OrderByDescending(r => r.MeetingDate).ThenByDescending(r => r.SubmittedAt).Take(200).ToListAsync(cancellationToken);
        if (urgentOnly)
        {
            reports = reports.Where(r => r.HasOpenUrgentFollowUp).ToList();
        }

        await audit.RecordAsync(new AuditRecord("groups.reports.listed", "cell_report", null, Details: new { cellId, count = reports.Count }, IsSensitiveRead: true), cancellationToken);
        return await ReportMapper.SummariesAsync(db, people, reports, cancellationToken);
    }

    public async Task<Result<ReportDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var report = await db.Reports.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id && r.Status == ReportStatus.Submitted, cancellationToken);
        if (report is null || !await authorizer.CanAsync(GroupsPermissions.ReportsView, ScopePath.Parse(report.Scope), cancellationToken))
        {
            return NotFound;
        }

        await audit.RecordAsync(new AuditRecord("groups.report.viewed", "cell_report", report.Id.ToString(), ScopePath.Parse(report.Scope), IsSensitiveRead: true), cancellationToken);
        return await ReportMapper.ToDtoAsync(db, people, report, cancellationToken);
    }

    public async Task<Result<ReportDto>> ResolveFollowUpAsync(Guid reportId, Guid followUpId, CancellationToken cancellationToken)
    {
        var report = await db.Reports.SingleOrDefaultAsync(r => r.Id == reportId && r.Status == ReportStatus.Submitted, cancellationToken);
        if (report is null || currentUser.UserId is not { } by
            || !await authorizer.CanAsync(GroupsPermissions.ReportsView, ScopePath.Parse(report.Scope), cancellationToken))
        {
            return NotFound;
        }

        report.ResolveFollowUp(followUpId, by, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord("groups.follow_up.resolved", "cell_report", report.Id.ToString(), ScopePath.Parse(report.Scope)), cancellationToken);
        return await ReportMapper.ToDtoAsync(db, people, report, cancellationToken);
    }

    public async Task<IReadOnlyList<MaterialDto>> MaterialsAsync(Guid? cellId, CancellationToken cancellationToken)
    {
        var scopes = await authorizer.ScopesForAsync(GroupsPermissions.ReportsView, cancellationToken);
        var query = db.Materials.AsNoTracking().WithinScopes(m => m.Scope, scopes);
        if (cellId is { } id)
        {
            query = query.Where(m => m.CellId == id);
        }

        var materials = await query.OrderByDescending(m => m.UpdatedAt).Take(200).ToListAsync(cancellationToken);
        return await ReportMapper.MaterialsAsync(db, people, materials, cancellationToken);
    }
}

/// <summary>Pastors: church lessons for every cell in a campus or the whole church, often drafted from Sunday's sermon.</summary>
public sealed class ChurchLessonService(
    IGroupsDb db,
    IPeopleDirectory people,
    IAuthorizer authorizer,
    ICurrentUser currentUser,
    IAuditLog audit,
    TimeProvider clock)
{
    private static readonly Error NotFound = Error.NotFound("groups.lesson_not_found", "Lesson not found.");

    public async Task<IReadOnlyList<MaterialDto>> ListAsync(CancellationToken cancellationToken)
    {
        var scopes = (await authorizer.ScopesForAsync(GroupsPermissions.CellsManage, cancellationToken))
            .Concat(await authorizer.ScopesForAsync(GroupsPermissions.ReportsView, cancellationToken))
            .ToList();
        var lessons = await db.Materials.AsNoTracking()
            .Where(m => m.CellId == null)
            .WithinScopes(m => m.Scope, scopes)
            .OrderByDescending(m => m.UpdatedAt)
            .Take(200)
            .ToListAsync(cancellationToken);
        return await ReportMapper.MaterialsAsync(db, people, lessons, cancellationToken);
    }

    public async Task<Result<MaterialDto>> CreateAsync(SaveLessonRequest request, CancellationToken cancellationToken)
    {
        var scopes = await authorizer.ScopesForAsync(GroupsPermissions.CellsManage, cancellationToken);
        ScopePath scope;
        if (string.IsNullOrWhiteSpace(request.Scope))
        {
            if (scopes.Count == 0)
            {
                return Error.Forbidden("groups.forbidden", "You can't write church lessons.");
            }

            scope = ScopeSet.Collapse(scopes)[0];
        }
        else if (!ScopePath.TryParse(request.Scope, out scope) || !await authorizer.CanAsync(GroupsPermissions.CellsManage, scope, cancellationToken))
        {
            return Error.Forbidden("groups.forbidden", "You can't write lessons for those cells.");
        }

        if (currentUser.PersonId is not { } me)
        {
            return Error.Unauthorized("groups.sign_in", "Sign in to write lessons.");
        }

        var lesson = CellMaterial.WriteChurchLesson(scope, request.Title, request.Body, request.Link, request.ForDate, request.SharedWithMembers, request.SermonId, me, clock.GetUtcNow());
        db.Materials.Add(lesson);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord("groups.lesson.created", "cell_material", lesson.Id.ToString(), scope, new { lesson.Title, lesson.SermonId }), cancellationToken);
        return (await ReportMapper.MaterialsAsync(db, people, [lesson], cancellationToken))[0];
    }

    public async Task<Result<MaterialDto>> UpdateAsync(Guid id, SaveLessonRequest request, CancellationToken cancellationToken)
    {
        var lesson = await db.Materials.SingleOrDefaultAsync(m => m.Id == id && m.CellId == null, cancellationToken);
        if (lesson is null || !await authorizer.CanAsync(GroupsPermissions.CellsManage, ScopePath.Parse(lesson.Scope), cancellationToken))
        {
            return NotFound;
        }

        lesson.Update(request.Title, request.Body, request.Link, request.ForDate, request.SharedWithMembers, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord("groups.lesson.updated", "cell_material", lesson.Id.ToString(), ScopePath.Parse(lesson.Scope)), cancellationToken);
        return (await ReportMapper.MaterialsAsync(db, people, [lesson], cancellationToken))[0];
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var lesson = await db.Materials.SingleOrDefaultAsync(m => m.Id == id && m.CellId == null, cancellationToken);
        if (lesson is null || !await authorizer.CanAsync(GroupsPermissions.CellsManage, ScopePath.Parse(lesson.Scope), cancellationToken))
        {
            return NotFound;
        }

        db.Materials.Remove(lesson);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord("groups.lesson.deleted", "cell_material", lesson.Id.ToString(), ScopePath.Parse(lesson.Scope), new { lesson.Title }), cancellationToken);
        return Result.Success();
    }
}

/// <summary>
/// Cell leaders: their own cell only. Leadership comes from being the cell's leader or co-leader (not a role grant),
/// and a two-step sign-in is required because reports hold special personal information.
/// </summary>
public sealed class CellLeaderService(
    IGroupsDb db,
    IPeopleDirectory people,
    IGuestRecords guests,
    ICurrentUser currentUser,
    IOptions<GroupsOptions> options,
    TimeProvider clock)
{
    private static readonly Error NotFound = Error.NotFound("groups.cell_not_found", "Cell not found.");
    private static readonly Error NeedsMfa = Error.Forbidden("groups.mfa_required", "Set up two-step verification and sign in with your authenticator code to open your cell.");

    public async Task<Result<CellDetailDto>> GetAsync(Guid cellId, CancellationToken cancellationToken)
    {
        var access = await LedCellAsync(cellId, tracked: false, cancellationToken);
        return access.IsFailure ? access.Error! : await CellDetails.ToDtoAsync(access.Value, people, cancellationToken);
    }

    public async Task<Result<CellDetailDto>> AddNewMemberAsync(Guid cellId, NewMemberRequest request, CancellationToken cancellationToken)
    {
        var access = await LedCellAsync(cellId, tracked: true, cancellationToken);
        if (access.IsFailure)
        {
            return access.Error!;
        }

        var person = await guests.CreateAsync(
            new GuestDetails(request.FirstName, request.LastName, request.Mobile, request.Email, EmailVerified: false, request.AgreedToBeOnRecord, GuestOrigin.CellGroup, FromWebsite: false, PolicyVersion: null),
            cancellationToken);
        if (person.IsFailure)
        {
            return person.Error!;
        }

        // Leaders add members; pastors choose leaders.
        return await ChangeAsync(access.Value, c => c.AddMember(person.Value, CellRole.Member, clock.GetUtcNow()), cancellationToken);
    }

    public async Task<Result<CellDetailDto>> RemoveMemberAsync(Guid cellId, Guid personId, CancellationToken cancellationToken)
    {
        var access = await LedCellAsync(cellId, tracked: true, cancellationToken);
        if (access.IsFailure)
        {
            return access.Error!;
        }

        var target = access.Value.ActiveMembers.SingleOrDefault(m => m.PersonId == personId);
        if (target is null)
        {
            return Error.NotFound("groups.not_a_member", "That person isn't in this cell.");
        }

        if (target.Role != CellRole.Member)
        {
            return Error.Forbidden("groups.leaders_by_pastors", "Leaders are changed by the pastors.");
        }

        return await ChangeAsync(access.Value, c => c.RemoveMember(personId, clock.GetUtcNow()), cancellationToken);
    }

    public async Task<Result<IReadOnlyList<ReportSummaryDto>>> ReportsAsync(Guid cellId, CancellationToken cancellationToken)
    {
        var access = await LedCellAsync(cellId, tracked: false, cancellationToken);
        if (access.IsFailure)
        {
            return access.Error!;
        }

        var reports = await db.Reports.AsNoTracking().Where(r => r.CellId == cellId).OrderByDescending(r => r.MeetingDate).Take(100).ToListAsync(cancellationToken);
        return Result<IReadOnlyList<ReportSummaryDto>>.Ok(await ReportMapper.SummariesAsync(db, people, reports, cancellationToken));
    }

    public async Task<Result<ReportDto>> ReportAsync(Guid cellId, Guid reportId, CancellationToken cancellationToken)
    {
        var access = await LedCellAsync(cellId, tracked: false, cancellationToken);
        if (access.IsFailure)
        {
            return access.Error!;
        }

        var report = await db.Reports.AsNoTracking().SingleOrDefaultAsync(r => r.Id == reportId && r.CellId == cellId, cancellationToken);
        return report is null ? Error.NotFound("groups.report_not_found", "Report not found.") : await ReportMapper.ToDtoAsync(db, people, report, cancellationToken);
    }

    public async Task<Result<ReportDto>> CreateReportAsync(Guid cellId, SaveReportRequest request, CancellationToken cancellationToken)
    {
        var access = await LedCellAsync(cellId, tracked: false, cancellationToken);
        if (access.IsFailure)
        {
            return access.Error!;
        }

        var cell = access.Value;
        if (await db.Reports.AnyAsync(r => r.CellId == cellId && r.MeetingDate == request.MeetingDate, cancellationToken))
        {
            return Error.Conflict("groups.report_exists", "There's already a report for that meeting. Open it to carry on.");
        }

        var now = clock.GetUtcNow();
        var report = CellReport.Start(cell.Id, ScopePath.Parse(cell.Scope), ToContent(request), cell.ActiveMembers.Select(m => m.PersonId).ToList(), currentUser.PersonId!.Value, now);
        if (request.Submit)
        {
            report.Submit(cell.Name, now);
        }

        db.Reports.Add(report);
        await db.SaveChangesAsync(cancellationToken);
        return await ReportMapper.ToDtoAsync(db, people, report, cancellationToken);
    }

    public async Task<Result<ReportDto>> UpdateReportAsync(Guid cellId, Guid reportId, SaveReportRequest request, CancellationToken cancellationToken)
    {
        var access = await LedCellAsync(cellId, tracked: false, cancellationToken);
        if (access.IsFailure)
        {
            return access.Error!;
        }

        var report = await db.Reports.SingleOrDefaultAsync(r => r.Id == reportId && r.CellId == cellId, cancellationToken);
        if (report is null)
        {
            return Error.NotFound("groups.report_not_found", "Report not found.");
        }

        if (request.MeetingDate != report.MeetingDate && await db.Reports.AnyAsync(r => r.CellId == cellId && r.MeetingDate == request.MeetingDate && r.Id != reportId, cancellationToken))
        {
            return Error.Conflict("groups.report_exists", "There's already a report for that meeting.");
        }

        var now = clock.GetUtcNow();
        report.Edit(ToContent(request), access.Value.ActiveMembers.Select(m => m.PersonId).ToList(), now);
        if (request.Submit)
        {
            report.Submit(access.Value.Name, now);
        }

        await db.SaveChangesAsync(cancellationToken);
        return await ReportMapper.ToDtoAsync(db, people, report, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<MaterialDto>>> MaterialsAsync(Guid cellId, CancellationToken cancellationToken)
    {
        var access = await LedCellAsync(cellId, tracked: false, cancellationToken);
        if (access.IsFailure)
        {
            return access.Error!;
        }

        var materials = await db.Materials.AsNoTracking().Where(m => m.CellId == cellId).OrderByDescending(m => m.UpdatedAt).ToListAsync(cancellationToken);
        return Result<IReadOnlyList<MaterialDto>>.Ok(await ReportMapper.MaterialsAsync(db, people, materials, cancellationToken));
    }

    /// <summary>The pastors' church lessons meant for this cell (its campus or the whole church), newest first.</summary>
    public async Task<Result<IReadOnlyList<MaterialDto>>> LessonsAsync(Guid cellId, CancellationToken cancellationToken)
    {
        var access = await LedCellAsync(cellId, tracked: false, cancellationToken);
        if (access.IsFailure)
        {
            return access.Error!;
        }

        var lessons = (await db.Materials.AsNoTracking().Where(m => m.CellId == null).OrderByDescending(m => m.UpdatedAt).Take(200).ToListAsync(cancellationToken))
            .Where(m => m.IsFor(access.Value.Scope))
            .Take(50)
            .ToList();
        return Result<IReadOnlyList<MaterialDto>>.Ok(await ReportMapper.MaterialsAsync(db, people, lessons, cancellationToken));
    }

    public async Task<Result<MaterialDto>> CreateMaterialAsync(Guid cellId, SaveMaterialRequest request, CancellationToken cancellationToken)
    {
        var access = await LedCellAsync(cellId, tracked: false, cancellationToken);
        if (access.IsFailure)
        {
            return access.Error!;
        }

        var material = CellMaterial.Write(cellId, ScopePath.Parse(access.Value.Scope), request.Title, request.Body, request.Link, request.ForDate, request.SharedWithMembers, currentUser.PersonId!.Value, clock.GetUtcNow());
        db.Materials.Add(material);
        await db.SaveChangesAsync(cancellationToken);
        return (await ReportMapper.MaterialsAsync(db, people, [material], cancellationToken))[0];
    }

    public async Task<Result<MaterialDto>> UpdateMaterialAsync(Guid cellId, Guid materialId, SaveMaterialRequest request, CancellationToken cancellationToken)
    {
        var access = await LedCellAsync(cellId, tracked: false, cancellationToken);
        if (access.IsFailure)
        {
            return access.Error!;
        }

        var material = await db.Materials.SingleOrDefaultAsync(m => m.Id == materialId && m.CellId == cellId, cancellationToken);
        if (material is null)
        {
            return Error.NotFound("groups.material_not_found", "Material not found.");
        }

        material.Update(request.Title, request.Body, request.Link, request.ForDate, request.SharedWithMembers, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return (await ReportMapper.MaterialsAsync(db, people, [material], cancellationToken))[0];
    }

    public async Task<Result> DeleteMaterialAsync(Guid cellId, Guid materialId, CancellationToken cancellationToken)
    {
        var access = await LedCellAsync(cellId, tracked: false, cancellationToken);
        if (access.IsFailure)
        {
            return access.Error!;
        }

        var deleted = await db.Materials.Where(m => m.Id == materialId && m.CellId == cellId).ExecuteDeleteAsync(cancellationToken);
        return deleted == 0 ? Error.NotFound("groups.material_not_found", "Material not found.") : Result.Success();
    }

    private async Task<Result<CellDetailDto>> ChangeAsync(Cell cell, Action<Cell> change, CancellationToken cancellationToken)
    {
        var before = cell.Members.Select(m => m.Id).ToHashSet();
        change(cell);
        CellDetails.TrackNewMembers(db, cell, before);
        await db.SaveChangesAsync(cancellationToken);
        return await CellDetails.ToDtoAsync(cell, people, cancellationToken);
    }

    /// <summary>The cell, if the signed-in person leads it (with a two-step sign-in when required).</summary>
    private async Task<Result<Cell>> LedCellAsync(Guid cellId, bool tracked, CancellationToken cancellationToken)
    {
        if (currentUser.PersonId is not { } me)
        {
            return Error.Unauthorized("groups.sign_in", "Sign in to open your cell.");
        }

        var query = tracked ? db.Cells : db.Cells.AsNoTracking();
        var cell = await query.Include(c => c.Members).SingleOrDefaultAsync(c => c.Id == cellId && c.Status == CellStatus.Active, cancellationToken);
        if (cell is null || !cell.IsLedBy(me))
        {
            return NotFound;
        }

        return options.Value.RequireMfaForLeaders && !currentUser.HasMfa ? NeedsMfa : cell;
    }

    private static ReportContent ToContent(SaveReportRequest r) =>
        new(r.MeetingDate, r.Topic, r.MaterialId, r.Notes, r.Highlights, r.PrayerNeeds, r.Multiplication, r.AttendeeIds ?? [], r.Visitors ?? [], r.FollowUps ?? [], r.Growth ?? []);
}

/// <summary>Anyone signed in: the cells they belong to, and the materials their leaders shared with them.</summary>
public sealed class MyCellsService(IGroupsDb db, IPeopleDirectory people, ICurrentUser currentUser)
{
    public async Task<Result<IReadOnlyList<MyCellDto>>> MineAsync(CancellationToken cancellationToken)
    {
        if (currentUser.PersonId is not { } me)
        {
            return Error.Unauthorized("groups.sign_in", "Sign in to see your cell.");
        }

        var cells = await db.Cells.AsNoTracking().Include(c => c.Members)
            .Where(c => c.Status == CellStatus.Active && c.Members.Any(m => m.PersonId == me && m.LeftAt == null))
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);
        var ids = cells.Select(c => c.Id).ToList();
        var shared = await db.Materials.AsNoTracking()
            .Where(m => m.SharedWithMembers && (m.CellId == null || ids.Contains(m.CellId.Value)))
            .OrderByDescending(m => m.UpdatedAt)
            .Take(200)
            .ToListAsync(cancellationToken);
        var materials = await ReportMapper.MaterialsAsync(db, people, shared, cancellationToken);
        var byId = shared.ToDictionary(m => m.Id);
        bool SharedWith(Cell cell, MaterialDto m) => m.CellId == cell.Id || byId[m.Id].IsFor(cell.Scope);
        var leaderIds = cells.SelectMany(c => c.ActiveMembers.Where(m => m.Role != CellRole.Member).Select(m => m.PersonId)).Distinct().ToList();
        var names = await people.GetManyAsync(leaderIds, cancellationToken);

        return cells.Select(c => new MyCellDto(
                c.Id,
                c.Name,
                c.MeetingDay,
                c.MeetingTime,
                c.Area,
                c.Address,
                c.ActiveMembers.Single(m => m.PersonId == me).Role,
                c.ActiveMembers.Where(m => m.Role != CellRole.Member).OrderBy(m => m.Role).Select(m => names.GetValueOrDefault(m.PersonId)?.DisplayName ?? "Unknown").ToList(),
                materials.Where(m => SharedWith(c, m)).ToList()))
            .ToList();
    }
}

internal static class CellDetails
{
    /// <summary>New members are attached to an already-tracked cell; tell the context they're new rows.</summary>
    public static void TrackNewMembers(IGroupsDb db, Cell cell, HashSet<Guid> before)
    {
        foreach (var member in cell.Members.Where(m => !before.Contains(m.Id)))
        {
            db.Members.Add(member);
        }
    }

    public static async Task<CellDetailDto> ToDtoAsync(Cell cell, IPeopleDirectory people, CancellationToken cancellationToken)
    {
        var active = cell.ActiveMembers.ToList();
        var names = await people.GetManyAsync(active.Select(m => m.PersonId).ToList(), cancellationToken);
        return new CellDetailDto(
            cell.Id,
            cell.Name,
            cell.CampusId,
            cell.MeetingDay,
            cell.MeetingTime,
            cell.Area,
            cell.Address,
            cell.Status,
            active.OrderBy(m => m.Role).ThenBy(m => names.GetValueOrDefault(m.PersonId)?.DisplayName)
                .Select(m => new CellMemberDto(m.PersonId, names.GetValueOrDefault(m.PersonId)?.DisplayName ?? "Unknown", m.Role, m.JoinedAt))
                .ToList());
    }
}

internal static class ReportMapper
{
    public static async Task<IReadOnlyList<ReportSummaryDto>> SummariesAsync(IGroupsDb db, IPeopleDirectory people, IReadOnlyList<CellReport> reports, CancellationToken cancellationToken)
    {
        var cellIds = reports.Select(r => r.CellId).Distinct().ToList();
        var cells = await db.Cells.AsNoTracking().Where(c => cellIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
        var writers = await people.GetManyAsync(reports.Select(r => r.WrittenByPersonId).Distinct().ToList(), cancellationToken);
        return reports.Select(r => new ReportSummaryDto(
                r.Id,
                r.CellId,
                cells.GetValueOrDefault(r.CellId) ?? "Cell",
                r.MeetingDate,
                r.Status,
                r.Topic,
                r.MembersPresent,
                r.VisitorCount,
                r.FollowUps.Count(f => f.Urgent && f.ResolvedAt is null),
                writers.GetValueOrDefault(r.WrittenByPersonId)?.DisplayName ?? "A leader",
                r.SubmittedAt,
                r.RedactedAt is not null))
            .ToList();
    }

    public static async Task<ReportDto> ToDtoAsync(IGroupsDb db, IPeopleDirectory people, CellReport r, CancellationToken cancellationToken)
    {
        var cellName = await db.Cells.AsNoTracking().Where(c => c.Id == r.CellId).Select(c => c.Name).SingleOrDefaultAsync(cancellationToken) ?? "Cell";
        var materialTitle = r.MaterialId is { } materialId
            ? await db.Materials.AsNoTracking().Where(m => m.Id == materialId).Select(m => m.Title).SingleOrDefaultAsync(cancellationToken)
            : null;
        var ids = r.AttendeeIds.Concat(r.Growth.Select(g => g.PersonId)).Append(r.WrittenByPersonId).Distinct().ToList();
        var names = await people.GetManyAsync(ids, cancellationToken);
        string Name(Guid id) => names.GetValueOrDefault(id)?.DisplayName ?? "Unknown";

        return new ReportDto(
            r.Id,
            r.CellId,
            cellName,
            r.MeetingDate,
            r.Status,
            r.Topic,
            r.MaterialId,
            materialTitle,
            r.Notes,
            r.Highlights,
            r.PrayerNeeds,
            r.Multiplication,
            r.AttendeeIds.Select(id => new PersonRefDto(id, Name(id))).OrderBy(p => p.Name).ToList(),
            r.Visitors,
            r.FollowUps,
            r.Growth.Select(g => new GrowthDto(g.PersonId, Name(g.PersonId), g.Step)).ToList(),
            r.MembersPresent,
            r.VisitorCount,
            Name(r.WrittenByPersonId),
            r.UpdatedAt,
            r.SubmittedAt,
            r.RedactedAt is not null);
    }

    public static async Task<IReadOnlyList<MaterialDto>> MaterialsAsync(IGroupsDb db, IPeopleDirectory people, IReadOnlyList<CellMaterial> materials, CancellationToken cancellationToken)
    {
        var cellIds = materials.Select(m => m.CellId).OfType<Guid>().Distinct().ToList();
        var cells = await db.Cells.AsNoTracking().Where(c => cellIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
        var writers = await people.GetManyAsync(materials.Select(m => m.WrittenByPersonId).Distinct().ToList(), cancellationToken);
        return materials.Select(m => new MaterialDto(
                m.Id,
                m.CellId,
                m.CellId is { } cellId ? cells.GetValueOrDefault(cellId) ?? "Cell" : "All cells",
                m.Title,
                m.Body,
                m.Link,
                m.ForDate,
                m.SharedWithMembers,
                writers.GetValueOrDefault(m.WrittenByPersonId)?.DisplayName ?? (m.IsChurchLesson ? "The pastors" : "A leader"),
                m.UpdatedAt,
                m.IsChurchLesson,
                m.SermonId))
            .ToList();
    }
}

/// <summary>Nightly: a year after a meeting, the personal parts of its report are removed (attendance numbers stay).</summary>
public sealed class CellReportRetentionJob(IGroupsDb db, TimeProvider clock)
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var cutoff = DateOnly.FromDateTime((now - CellReport.Retention).UtcDateTime);
        var due = await db.Reports.Where(r => r.RedactedAt == null && r.MeetingDate < cutoff).Take(500).ToListAsync(cancellationToken);
        var redacted = due.Count(r => r.Redact(now));
        await db.SaveChangesAsync(cancellationToken);
        return redacted;
    }
}

/// <summary>A person's cells and the reports that mention them, for export and erasure.</summary>
public sealed class GroupsPersonalData(IGroupsDb db) : Shapers.Platform.Privacy.IPersonalDataSource
{
    public string Name => "Home cells";

    public async Task<object?> ExportAsync(Guid personId, CancellationToken cancellationToken)
    {
        var memberships = await db.Members.AsNoTracking().Where(m => m.PersonId == personId)
            .Join(db.Cells.AsNoTracking(), m => m.CellId, c => c.Id, (m, c) => new { Cell = c.Name, Role = m.Role.ToString(), m.JoinedAt, m.LeftAt })
            .ToListAsync(cancellationToken);
        var reports = await db.Reports.AsNoTracking().Where(r => r.AttendeeIds.Contains(personId)).Select(r => r.MeetingDate).ToListAsync(cancellationToken);
        return memberships.Count == 0 && reports.Count == 0 ? null : new { Memberships = memberships, MeetingsAttended = reports.OrderBy(d => d).ToList() };
    }

    public async Task<int> EraseAsync(Guid personId, CancellationToken cancellationToken)
    {
        // Only reports of cells they belonged to can mention them: reports accept the cell's members only.
        var cellIds = await db.Members.Where(m => m.PersonId == personId).Select(m => m.CellId).Distinct().ToListAsync(cancellationToken);
        var reports = await db.Reports.Where(r => cellIds.Contains(r.CellId) && r.RedactedAt == null).ToListAsync(cancellationToken);
        var removed = reports.Count(r => r.RemovePerson(personId));
        await db.SaveChangesAsync(cancellationToken);
        return removed + await db.Members.Where(m => m.PersonId == personId).ExecuteDeleteAsync(cancellationToken);
    }
}

/// <summary>Two church records were merged: memberships and report mentions move to the surviving record.</summary>
public sealed class ReplaceMergedPerson(IGroupsDb db, TimeProvider clock) : IIntegrationEventHandler<PeopleMergedIntegrationEvent>
{
    public async Task HandleAsync(PeopleMergedIntegrationEvent e, CancellationToken cancellationToken)
    {
        var cells = await db.Cells.Include(c => c.Members).Where(c => c.Members.Any(m => m.PersonId == e.MergedId)).ToListAsync(cancellationToken);
        foreach (var cell in cells)
        {
            cell.ReplacePerson(e.MergedId, e.SurvivorId, clock.GetUtcNow());
        }

        var cellIds = cells.Select(c => c.Id).ToList();
        var reports = await db.Reports.Where(r => cellIds.Contains(r.CellId) && r.RedactedAt == null).ToListAsync(cancellationToken);
        foreach (var report in reports)
        {
            report.ReplacePerson(e.MergedId, e.SurvivorId);
        }

        await db.Materials.Where(m => m.WrittenByPersonId == e.MergedId).ExecuteUpdateAsync(s => s.SetProperty(m => m.WrittenByPersonId, e.SurvivorId), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }
}
