using Microsoft.EntityFrameworkCore;
using Shapers.Church.Contracts;
using Shapers.Identity.Contracts;
using Shapers.People.Domain;

namespace Shapers.People.Application;

/// <summary>Assembles read models for people. Shared by the staff and member-facing services.</summary>
public sealed class PersonDetailBuilder(IPeopleDb db, IChurchDirectory church, IUserDirectory users)
{
    public async Task<PersonDetailDto> BuildAsync(Person person, CancellationToken cancellationToken)
    {
        var statuses = await StatusesAsync(cancellationToken);
        var campuses = await church.GetCampusesAsync(cancellationToken);

        var households = await db.Households.AsNoTracking()
            .Where(h => h.Members.Any(m => m.PersonId == person.Id))
            .ToListAsync(cancellationToken);
        var memberIds = households.SelectMany(h => h.Members).Select(m => m.PersonId).Distinct().ToList();
        var memberNames = await db.Persons.AsNoTracking()
            .Where(p => memberIds.Contains(p.Id))
            .Select(p => new { p.Id, Name = (p.PreferredName ?? p.FirstName) + " " + p.LastName })
            .ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);

        var consentHistory = await db.ConsentRecords.AsNoTracking()
            .Where(c => c.PersonId == person.Id)
            .ToListAsync(cancellationToken);

        var status = statuses[person.MembershipStatusId];
        return new PersonDetailDto(
            person.Id,
            person.FirstName,
            person.LastName,
            person.PreferredName,
            person.DisplayName,
            person.DateOfBirth,
            person.Gender,
            person.Scope,
            CampusName(campuses, person.Scope),
            ToDto(status),
            person.Status,
            person.MergedIntoId,
            person.Source,
            await users.GetUserIdForPersonAsync(person.Id, cancellationToken) is not null,
            person.Contacts
                .OrderBy(c => c.Type).ThenByDescending(c => c.IsPrimary)
                .Select(c => new ContactDto(c.Id, c.Type, c.Value, c.IsPrimary, c.IsVerified))
                .ToList(),
            households.Select(h => ToDto(h, memberNames)).ToList(),
            person.StatusHistory
                .OrderByDescending(h => h.RecordedAt)
                .Select(h => new StatusChangeDto(
                    h.FromStatusId is { } from && statuses.TryGetValue(from, out var f) ? f.Name : null,
                    statuses.TryGetValue(h.ToStatusId, out var t) ? t.Name : "Unknown",
                    h.EffectiveDate,
                    h.RecordedAt))
                .ToList(),
            ConsentRecord.Current(consentHistory).Values
                .OrderBy(c => c.Purpose)
                .Select(c => new ConsentDto(c.Purpose, c.Granted, c.LawfulBasis, c.PolicyVersion, c.Source, c.RecordedAt))
                .ToList(),
            person.CreatedAt,
            person.UpdatedAt);
    }

    public async Task<IReadOnlyList<PersonListItemDto>> BuildListAsync(IReadOnlyList<Person> people, CancellationToken cancellationToken)
    {
        var statuses = await StatusesAsync(cancellationToken);
        var campuses = await church.GetCampusesAsync(cancellationToken);
        return people.Select(p => ToListItem(p, statuses, campuses)).ToList();
    }

    public static PersonListItemDto ToListItem(Person p, IReadOnlyDictionary<Guid, MembershipStatus> statuses, IReadOnlyList<CampusSummary> campuses)
    {
        var status = statuses.TryGetValue(p.MembershipStatusId, out var s) ? s : null;
        return new PersonListItemDto(
            p.Id,
            p.DisplayName,
            p.FirstName,
            p.LastName,
            p.Scope,
            CampusName(campuses, p.Scope),
            status?.Name ?? "Unknown",
            (status?.Stage ?? JourneyStage.Visitor).ToString(),
            p.Status.ToString(),
            p.PrimaryContact(ContactType.Email)?.Value,
            p.PrimaryContact(ContactType.Mobile)?.Value);
    }

    public async Task<IReadOnlyDictionary<Guid, MembershipStatus>> StatusesAsync(CancellationToken cancellationToken) =>
        await db.MembershipStatuses.AsNoTracking().ToDictionaryAsync(s => s.Id, cancellationToken);

    public static MembershipStatusDto ToDto(MembershipStatus s) => new(s.Id, s.Name, s.Stage, s.SortOrder, s.IsDefault, s.IsActive);

    public static HouseholdDto ToDto(Household h, IReadOnlyDictionary<Guid, string> names) =>
        new(
            h.Id,
            h.Name,
            h.Scope,
            h.Members
                .OrderBy(m => m.Role)
                .Select(m => new HouseholdMemberDto(m.PersonId, names.GetValueOrDefault(m.PersonId, "Unknown"), m.Role, h.PrimaryContactId == m.PersonId))
                .ToList());

    private static string? CampusName(IReadOnlyList<CampusSummary> campuses, string scope) =>
        campuses.FirstOrDefault(c => scope == c.Scope || scope.StartsWith(c.Scope + ".", StringComparison.Ordinal))?.Name;
}
