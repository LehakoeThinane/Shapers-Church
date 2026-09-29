using Shapers.People.Domain;

namespace Shapers.People.Application;

public sealed record PersonListItemDto(
    Guid Id,
    string DisplayName,
    string FirstName,
    string LastName,
    string Scope,
    string? CampusName,
    string MembershipStatus,
    string Stage,
    string Status,
    string? PrimaryEmail,
    string? PrimaryMobile);

public sealed record ContactDto(Guid Id, ContactType Type, string Value, bool IsPrimary, bool IsVerified);

public sealed record MembershipStatusDto(Guid Id, string Name, JourneyStage Stage, int SortOrder, bool IsDefault, bool IsActive);

public sealed record StatusChangeDto(string? From, string To, DateOnly EffectiveDate, DateTimeOffset RecordedAt);

public sealed record HouseholdMemberDto(Guid PersonId, string DisplayName, HouseholdRole Role, bool IsPrimaryContact);

public sealed record HouseholdDto(Guid Id, string Name, string Scope, IReadOnlyList<HouseholdMemberDto> Members);

public sealed record ConsentDto(string Purpose, bool Granted, LawfulBasis LawfulBasis, string PolicyVersion, ConsentSource Source, DateTimeOffset RecordedAt);

public sealed record PersonDetailDto(
    Guid Id,
    string FirstName,
    string LastName,
    string? PreferredName,
    string DisplayName,
    DateOnly? DateOfBirth,
    Gender? Gender,
    string Scope,
    string? CampusName,
    MembershipStatusDto MembershipStatus,
    PersonStatus Status,
    Guid? MergedIntoId,
    PersonSource Source,
    bool HasLogin,
    IReadOnlyList<ContactDto> Contacts,
    IReadOnlyList<HouseholdDto> Households,
    IReadOnlyList<StatusChangeDto> StatusHistory,
    IReadOnlyList<ConsentDto> Consents,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreatePersonRequest(
    string FirstName,
    string LastName,
    string? PreferredName,
    DateOnly? DateOfBirth,
    Gender? Gender,
    Guid? CampusId,
    Guid? MembershipStatusId,
    string? Email,
    string? Mobile);

public sealed record UpdatePersonRequest(string FirstName, string LastName, string? PreferredName, DateOnly? DateOfBirth, Gender? Gender);

public sealed record AddContactRequest(ContactType Type, string Value, bool IsPrimary);

public sealed record ChangeStatusRequest(Guid MembershipStatusId, DateOnly? EffectiveDate);

public sealed record MoveCampusRequest(Guid CampusId);

public sealed record CreateHouseholdRequest(string Name, Guid? CampusId, IReadOnlyList<HouseholdMemberRequest> Members);

public sealed record HouseholdMemberRequest(Guid PersonId, HouseholdRole Role);

public sealed record MergeRequest(Guid SurvivorId, Guid DuplicateId);

public sealed record DuplicateCandidateDto(Guid Id, PersonListItemDto PersonA, PersonListItemDto PersonB, int Score, string Reasons, DateTimeOffset DetectedAt);

public sealed record RecordConsentRequest(IReadOnlyList<ConsentDecisionDto> Decisions, string PolicyVersion, ConsentSource Source, LawfulBasis LawfulBasis = LawfulBasis.Consent);

public sealed record ConsentDecisionDto(string Purpose, bool Granted);

public sealed record UpdateMyProfileRequest(string FirstName, string LastName, string? PreferredName, DateOnly? DateOfBirth);

public sealed record CreateMembershipStatusRequest(string Name, JourneyStage Stage, int SortOrder);

public sealed record PeopleListQuery(string? Search, string? Scope, Guid? MembershipStatusId, bool IncludeInactive = false, int Page = 1, int PageSize = 25);
