using Microsoft.EntityFrameworkCore;
using Shapers.People.Contracts;
using Shapers.People.Domain;

namespace Shapers.People.Application;

/// <summary>Children added by their parents (in the app or at the kids desk), and the children in a parent's households.</summary>
public sealed class FamilyRecords(IPeopleDb db, DuplicateDetector duplicates, TimeProvider clock) : IFamilyRecords
{
    /// <summary>The privacy notice in force when the guardian consented.</summary>
    public const string PolicyVersion = "2026-09";

    public async Task<Result<IReadOnlyList<Guid>>> AddChildrenAsync(
        Guid parentId, IReadOnlyList<ChildDetails> children, Guid? recordedByUserId, CancellationToken cancellationToken = default)
    {
        var parent = await db.Persons.AsNoTracking().SingleOrDefaultAsync(p => p.Id == parentId && p.Status == PersonStatus.Active, cancellationToken);
        if (parent is null)
        {
            return Error.NotFound("people.not_found", "Person not found.");
        }

        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        if (parent.IsMinorOn(today))
        {
            return new Error("people.guardian_must_be_adult", "Only a parent or guardian over 18 can add a child.");
        }

        if (children.Count == 0)
        {
            return new Error("people.child_required", "Add at least one child.");
        }

        if (children.FirstOrDefault(c => c.DateOfBirth > today || c.DateOfBirth < today.AddYears(-18)) is { } wrong)
        {
            return new Error("people.child_dob_invalid", $"Check {wrong.FirstName}'s date of birth: children's church is for children under 18.");
        }

        var status = await db.MembershipStatuses.SingleAsync(s => s.IsDefault, cancellationToken);
        var people = children.Select(child =>
        {
            var person = Person.Create(ScopePath.Parse(parent.Scope), child.FirstName, child.LastName, status, PersonSource.Guardian, now, recordedByUserId);
            person.SetDemographics(child.DateOfBirth, null, now);
            return person;
        }).ToList();
        db.Persons.AddRange(people);

        // One save for the household and every child: its members share the household's version check.
        var household = await db.Households
            .Where(h => h.Members.Any(m => m.PersonId == parentId && m.Role == HouseholdRole.Adult))
            .OrderBy(h => h.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (household is null)
        {
            household = Household.Create($"{parent.LastName} family", ScopePath.Parse(parent.Scope), now);
            household.AddMember(parentId, HouseholdRole.Adult);
            db.Households.Add(household);
        }

        foreach (var person in people)
        {
            household.AddMember(person.Id, HouseholdRole.Child);

            // A child can't consent for themselves: the parent or guardian does, and that is what is recorded.
            db.ConsentRecords.Add(ConsentRecord.Record(
                person.Id,
                ConsentPurposes.ChurchRecord,
                granted: true,
                LawfulBasis.Consent,
                PolicyVersion,
                recordedByUserId is null ? ConsentSource.MobileApp : ConsentSource.AdminPortal,
                now,
                recordedByUserId));
        }

        await db.SaveChangesAsync(cancellationToken);
        foreach (var person in people)
        {
            await duplicates.DetectAsync(person, cancellationToken);
        }

        return people.Select(p => p.Id).ToList();
    }

    public async Task<IReadOnlyList<ChildSummary>> ChildrenOfAsync(Guid parentId, CancellationToken cancellationToken = default)
    {
        var households = await db.Households.AsNoTracking()
            .Where(h => h.Members.Any(m => m.PersonId == parentId && m.Role == HouseholdRole.Adult))
            .ToListAsync(cancellationToken);
        var members = households.SelectMany(h => h.Members).Where(m => m.PersonId != parentId).DistinctBy(m => m.PersonId).ToList();
        var ids = members.Select(m => m.PersonId).ToList();
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var people = await db.Persons.AsNoTracking().Where(p => ids.Contains(p.Id) && p.Status == PersonStatus.Active).ToListAsync(cancellationToken);
        var childIds = members.Where(m => m.Role == HouseholdRole.Child).Select(m => m.PersonId).ToHashSet();
        return people
            .Where(p => childIds.Contains(p.Id) || p.IsMinorOn(today))
            .Select(ToSummary)
            .OrderBy(c => c.DateOfBirth ?? DateOnly.MaxValue)
            .ToList();
    }

    public async Task<IReadOnlyDictionary<Guid, ChildSummary>> GetChildrenAsync(IReadOnlyCollection<Guid> childIds, CancellationToken cancellationToken = default) =>
        (await db.Persons.AsNoTracking().Where(p => childIds.Contains(p.Id)).ToListAsync(cancellationToken)).ToDictionary(p => p.Id, ToSummary);

    private static ChildSummary ToSummary(Person p) => new(p.Id, p.PreferredName ?? p.FirstName, p.DisplayName, p.DateOfBirth, p.Scope);
}
