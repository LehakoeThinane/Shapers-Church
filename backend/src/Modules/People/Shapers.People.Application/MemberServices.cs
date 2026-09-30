using Microsoft.EntityFrameworkCore;
using Shapers.Church.Contracts;
using Shapers.Identity.Contracts;
using Shapers.People.Contracts;
using Shapers.People.Domain;
using Shapers.Platform.Authorization;
using Shapers.Platform.Text;

namespace Shapers.People.Application;

/// <summary>A signed-in member's own record: the PERSONAL scope. No staff permission is involved.</summary>
public sealed class MyProfileService(IPeopleDb db, ICurrentUser currentUser, PersonDetailBuilder builder, TimeProvider clock)
{
    private static readonly Error NoProfile = Error.NotFound("people.no_profile", "Your account is not linked to a church record.");

    public async Task<Result<PersonDetailDto>> GetAsync(CancellationToken cancellationToken)
    {
        var person = await LoadAsync(tracking: false, cancellationToken);
        return person is null ? NoProfile : await builder.BuildAsync(person, cancellationToken);
    }

    public async Task<Result<PersonDetailDto>> UpdateAsync(UpdateMyProfileRequest request, CancellationToken cancellationToken)
    {
        var person = await LoadAsync(tracking: true, cancellationToken);
        if (person is null)
        {
            return NoProfile;
        }

        var now = clock.GetUtcNow();
        person.Rename(request.FirstName, request.LastName, request.PreferredName, now);
        person.SetDemographics(request.DateOfBirth, person.Gender, now);
        await db.SaveChangesAsync(cancellationToken);
        return await builder.BuildAsync(person, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<ConsentDto>>> RecordConsentAsync(IReadOnlyList<ConsentDecisionDto> decisions, string policyVersion, ConsentSource source, CancellationToken cancellationToken)
    {
        if (currentUser.PersonId is not { } personId)
        {
            return NoProfile;
        }

        var now = clock.GetUtcNow();
        foreach (var decision in decisions)
        {
            db.ConsentRecords.Add(ConsentRecord.Record(personId, decision.Purpose, decision.Granted, LawfulBasis.Consent, policyVersion, source, now, recordedBy: null));
        }

        await db.SaveChangesAsync(cancellationToken);
        var history = await db.ConsentRecords.AsNoTracking().Where(c => c.PersonId == personId).ToListAsync(cancellationToken);
        return ConsentRecord.Current(history).Values
            .Select(c => new ConsentDto(c.Purpose, c.Granted, c.LawfulBasis, c.PolicyVersion, c.Source, c.RecordedAt))
            .ToList();
    }

    private async Task<Person?> LoadAsync(bool tracking, CancellationToken cancellationToken)
    {
        if (currentUser.PersonId is not { } personId)
        {
            return null;
        }

        var query = tracking ? db.Persons : db.Persons.AsNoTracking();
        return await query.SingleOrDefaultAsync(p => p.Id == personId && p.Status != PersonStatus.Merged, cancellationToken);
    }
}

public sealed class PeopleDirectory(IPeopleDb db, TimeProvider clock) : IPeopleDirectory
{
    private const int MaxMergeHops = 10;

    public async Task<IReadOnlySet<Guid>> WithConsentAsync(IReadOnlyCollection<Guid> personIds, string purpose, CancellationToken cancellationToken = default)
    {
        var latest = await db.ConsentRecords.AsNoTracking()
            .Where(c => personIds.Contains(c.PersonId) && c.Purpose == purpose)
            .GroupBy(c => c.PersonId)
            .Select(g => g.OrderByDescending(c => c.RecordedAt).ThenByDescending(c => c.Id).First())
            .ToListAsync(cancellationToken);
        return latest.Where(c => c.Granted).Select(c => c.PersonId).ToHashSet();
    }

    public async Task<IReadOnlyDictionary<Guid, PersonSummary>> GetManyAsync(IReadOnlyCollection<Guid> personIds, CancellationToken cancellationToken = default) =>
        await db.Persons.AsNoTracking()
            .Where(p => personIds.Contains(p.Id))
            .Select(p => new PersonSummary(
                p.Id,
                (p.PreferredName ?? p.FirstName) + " " + p.LastName,
                p.Scope,
                p.Status.ToString(),
                p.MergedIntoId,
                p.Contacts.Where(c => c.Type == ContactType.Email).OrderByDescending(c => c.IsPrimary).Select(c => c.Value).FirstOrDefault()))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

    public async Task<IReadOnlyList<HouseholdMemberSummary>> GetHouseholdMembersAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        var households = await db.Households.AsNoTracking().Where(h => h.Members.Any(m => m.PersonId == personId)).ToListAsync(cancellationToken);
        var members = households.SelectMany(h => h.Members).Where(m => m.PersonId != personId).DistinctBy(m => m.PersonId).ToList();
        var ids = members.Select(m => m.PersonId).ToList();
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var people = await db.Persons.AsNoTracking()
            .Where(p => ids.Contains(p.Id) && p.Status == PersonStatus.Active)
            .ToDictionaryAsync(p => p.Id, cancellationToken);
        return members
            .Where(m => people.ContainsKey(m.PersonId))
            .Select(m => new HouseholdMemberSummary(m.PersonId, people[m.PersonId].DisplayName, m.Role == HouseholdRole.Child || people[m.PersonId].IsMinorOn(today)))
            .OrderBy(m => m.IsChild).ThenBy(m => m.DisplayName)
            .ToList();
    }

    public async Task<PersonSummary?> GetAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        var id = personId;
        for (var hop = 0; hop < MaxMergeHops; hop++)
        {
            var person = await db.Persons.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
            if (person is null)
            {
                return null;
            }

            if (person.MergedIntoId is not { } next)
            {
                return new PersonSummary(person.Id, person.DisplayName, person.Scope, person.Status.ToString(), null, person.PrimaryContact(ContactType.Email)?.Value);
            }

            id = next;
        }

        throw new InvalidOperationException($"Merge chain from {personId} is longer than {MaxMergeHops} hops.");
    }
}

public sealed class PeopleRegistration(
    IPeopleDb db,
    IUserDirectory users,
    IChurchDirectory church,
    DuplicateDetector duplicates,
    TimeProvider clock) : IPeopleRegistration
{
    public async Task<Guid?> FindLinkablePersonAsync(string verifiedPhone, CancellationToken cancellationToken = default)
    {
        if (!ContactNormaliser.TryNormalisePhone(verifiedPhone, out var phone))
        {
            return null;
        }

        var matches = await db.Persons.AsNoTracking()
            .Where(p => p.Status == PersonStatus.Active && p.Contacts.Any(c => c.Value == phone && (c.Type == ContactType.Mobile || c.Type == ContactType.WhatsApp)))
            .OrderBy(p => p.Id)
            .Take(2)
            .ToListAsync(cancellationToken);

        // Families often share one phone number. Only an unambiguous adult match is safe to hand over.
        if (matches.Count != 1)
        {
            return null;
        }

        var person = matches[0];
        if (person.IsMinorOn(DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime)))
        {
            return null;
        }

        var isChildSomewhere = await db.Households.AnyAsync(
            h => h.Members.Any(m => m.PersonId == person.Id && m.Role == HouseholdRole.Child), cancellationToken);
        if (isChildSomewhere)
        {
            return null;
        }

        return await users.GetUserIdForPersonAsync(person.Id, cancellationToken) is null ? person.Id : null;
    }

    public async Task<RegistrationOutcome> RegisterAsync(SelfRegistration registration, CancellationToken cancellationToken = default)
    {
        var churchRecordConsent = registration.Consents.FirstOrDefault(c => c.Purpose == ConsentPurposes.ChurchRecord);
        if (churchRecordConsent is not { Granted: true })
        {
            throw new DomainRuleException(
                "people.consent_required",
                "We need your consent to keep a church record before we can create your account.");
        }

        if (!ContactNormaliser.TryNormalisePhone(registration.VerifiedPhone, out var phone))
        {
            throw new DomainRuleException("people.mobile_invalid", "That mobile number doesn't look right.");
        }

        string? email = null;
        if (!string.IsNullOrWhiteSpace(registration.Email) && !ContactNormaliser.TryNormaliseEmail(registration.Email, out email))
        {
            throw new DomainRuleException("people.email_invalid", "That email address doesn't look right.");
        }

        var now = clock.GetUtcNow();
        var linkableId = await FindLinkablePersonAsync(phone, cancellationToken);
        Person person;
        if (linkableId is { } existingId)
        {
            person = await db.Persons.SingleAsync(p => p.Id == existingId, cancellationToken);
        }
        else
        {
            var scope = await ResolveScopeAsync(registration.CampusId, cancellationToken);
            var status = await db.MembershipStatuses.SingleAsync(s => s.IsDefault, cancellationToken);
            person = Person.Create(scope, registration.FirstName, registration.LastName, status, PersonSource.SelfRegistration, now);
            db.Persons.Add(person);
        }

        person.AddContact(ContactType.Mobile, phone, isPrimary: true, isVerified: true, now);
        if (email is not null)
        {
            person.AddContact(ContactType.Email, email, isPrimary: linkableId is null, isVerified: false, now);
        }

        foreach (var consent in registration.Consents)
        {
            db.ConsentRecords.Add(ConsentRecord.Record(
                person.Id, consent.Purpose, consent.Granted, LawfulBasis.Consent, registration.PolicyVersion, ConsentSource.MobileApp, now, recordedBy: null));
        }

        await db.SaveChangesAsync(cancellationToken);
        if (linkableId is null)
        {
            await duplicates.DetectAsync(person, cancellationToken);
        }

        return new RegistrationOutcome(person.Id, LinkedExistingRecord: linkableId is not null);
    }

    public async Task<Guid> EnsureStaffRecordAsync(string firstName, string lastName, string email, CancellationToken cancellationToken = default)
    {
        if (!ContactNormaliser.TryNormaliseEmail(email, out var normalised))
        {
            throw new DomainRuleException("people.email_invalid", "That email address doesn't look right.");
        }

        var matches = await db.Persons.AsNoTracking()
            .Where(p => p.Status == PersonStatus.Active && p.Contacts.Any(c => c.Type == ContactType.Email && c.Value == normalised))
            .Select(p => p.Id)
            .OrderBy(id => id)
            .Take(2)
            .ToListAsync(cancellationToken);
        if (matches.Count == 1)
        {
            return matches[0];
        }

        var now = clock.GetUtcNow();
        var status = await db.MembershipStatuses
            .Where(s => s.Stage == JourneyStage.Member && s.IsActive)
            .OrderBy(s => s.SortOrder)
            .FirstAsync(cancellationToken);
        var person = Person.Create(await ResolveScopeAsync(null, cancellationToken), firstName, lastName, status, PersonSource.Admin, now);
        person.AddContact(ContactType.Email, normalised, isPrimary: true, isVerified: false, now);
        db.Persons.Add(person);
        await db.SaveChangesAsync(cancellationToken);
        return person.Id;
    }

    private async Task<ScopePath> ResolveScopeAsync(Guid? campusId, CancellationToken cancellationToken)
    {
        var campuses = await church.GetCampusesAsync(cancellationToken);
        var campus = campuses.FirstOrDefault(c => c.Id == campusId) ?? campuses.FirstOrDefault(c => c.IsPrimary) ?? (campuses.Count > 0 ? campuses[0] : null);
        return campus is not null
            ? ScopePath.Parse(campus.Scope)
            : ScopePath.Parse((await church.GetRootScopeAsync(cancellationToken)).Path);
    }
}
