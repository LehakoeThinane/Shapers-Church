namespace Shapers.People.Contracts;

public static class PeoplePermissions
{
    /// <summary>Church records reveal religious belief, so viewing them is sensitive under POPIA.</summary>
    public const string ProfilesView = "people.profiles.view";
    public const string ProfilesEdit = "people.profiles.edit";
    public const string ProfilesMerge = "people.profiles.merge";
    public const string StatusesManage = "people.statuses.manage";
}

public sealed record PersonCreatedIntegrationEvent(Guid PersonId, string Scope, string Source) : IntegrationEvent;

/// <summary>
/// Another module holding <see cref="MergedId"/> must re-point it to <see cref="SurvivorId"/>.
/// The merged ID keeps resolving (as a tombstone) while that happens.
/// </summary>
public sealed record PeopleMergedIntegrationEvent(Guid SurvivorId, Guid MergedId) : IntegrationEvent;

public sealed record MembershipStatusChangedIntegrationEvent(Guid PersonId, string ToStage) : IntegrationEvent;

/// <summary>
/// Someone raised a hand on a connect card. Notifications will tell the campus follow-up team.
/// PrayerText is set only when they asked for prayer, so the Prayer module can file it with the pastors.
/// </summary>
public sealed record ConnectCardSubmittedIntegrationEvent(Guid CardId, Guid PersonId, string Scope, IReadOnlyList<string> Reasons, string? PrayerText = null) : IntegrationEvent;

public sealed record PersonMovedCampusIntegrationEvent(Guid PersonId, string FromScope, string ToScope) : IntegrationEvent;

public sealed record PersonSummary(Guid Id, string DisplayName, string Scope, string Status, Guid? MergedIntoId, string? Email = null);

public sealed record ConsentDecision(string Purpose, bool Granted);

/// <summary>A member signing up in the app after proving they own <see cref="VerifiedPhone"/>.</summary>
public sealed record SelfRegistration(
    string FirstName,
    string LastName,
    string VerifiedPhone,
    string? Email,
    Guid? CampusId,
    string PolicyVersion,
    IReadOnlyList<ConsentDecision> Consents);

public sealed record RegistrationOutcome(Guid PersonId, bool LinkedExistingRecord);

public sealed record HouseholdMemberSummary(Guid PersonId, string DisplayName, bool IsChild);

public interface IPeopleDirectory
{
    /// <summary>Follows merge tombstones, so a stale ID still returns the surviving person.</summary>
    Task<PersonSummary?> GetAsync(Guid personId, CancellationToken cancellationToken = default);

    /// <summary>Summaries for many people at once (unknown IDs are skipped).</summary>
    Task<IReadOnlyDictionary<Guid, PersonSummary>> GetManyAsync(IReadOnlyCollection<Guid> personIds, CancellationToken cancellationToken = default);

    /// <summary>Everyone who shares a household with this person, excluding them.</summary>
    Task<IReadOnlyList<HouseholdMemberSummary>> GetHouseholdMembersAsync(Guid personId, CancellationToken cancellationToken = default);
}

public enum GuestOrigin
{
    ConnectCard,
    EventRegistration,
}

/// <summary>Someone who isn't signed in, giving their details with consent (e.g. registering for an event).</summary>
public sealed record GuestDetails(
    string? FirstName,
    string? LastName,
    string? Mobile,
    string? Email,
    bool EmailVerified,
    bool ConsentToKeepDetails,
    GuestOrigin Origin,
    bool FromWebsite,
    string? PolicyVersion);

public interface IGuestRecords
{
    /// <summary>
    /// Creates a new church record for a guest. Never attaches to an existing record (anyone can type anyone's
    /// details); a possible duplicate is flagged for staff to review instead.
    /// </summary>
    Task<Result<Guid>> CreateAsync(GuestDetails guest, CancellationToken cancellationToken = default);
}

public interface IPeopleRegistration
{
    /// <summary>
    /// Finds the church record for a newly verified member, or creates one. Links to an existing record only
    /// when exactly one adult record has that phone number and no login; otherwise creates a new record and
    /// flags a possible duplicate for staff to review.
    /// </summary>
    Task<RegistrationOutcome> RegisterAsync(SelfRegistration registration, CancellationToken cancellationToken = default);

    /// <summary>For a returning member whose phone matches a record: the person to link, if the rules above allow it.</summary>
    Task<Guid?> FindLinkablePersonAsync(string verifiedPhone, CancellationToken cancellationToken = default);

    /// <summary>
    /// Used when setting up the first administrator: returns the one active record with this email,
    /// or creates one at the primary campus.
    /// </summary>
    Task<Guid> EnsureStaffRecordAsync(string firstName, string lastName, string email, CancellationToken cancellationToken = default);
}
