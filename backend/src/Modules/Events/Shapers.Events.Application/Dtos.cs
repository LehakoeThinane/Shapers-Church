using Shapers.Events.Domain;

namespace Shapers.Events.Application;

public sealed record QuestionDto(Guid Id, string Label, bool Required);

public sealed record LocationDto(string Name, string? Address);

/// <summary>What anyone sees about an event, including whether they can register right now.</summary>
public sealed record EventDto(
    Guid Id,
    string Title,
    string Slug,
    string? Summary,
    string? Description,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    LocationDto? Location,
    string? ImageUrl,
    EventVisibility Visibility,
    bool RegistrationRequired,
    bool RegistrationOpen,
    string? RegistrationClosedReason,
    int? SeatsLeft,
    bool WaitlistOnly,
    int MaxPerRegistration,
    IReadOnlyList<QuestionDto> Questions);

public sealed record EventAdminDto(
    EventDto Event,
    EventStatus Status,
    string Scope,
    DateTimeOffset? RegistrationOpensAt,
    DateTimeOffset? RegistrationClosesAt,
    int? Capacity,
    bool WaitlistEnabled,
    int Confirmed,
    int Waitlisted,
    int CheckedIn);

public sealed record QuestionInput(Guid? Id, string Label, bool Required);

public sealed record SaveEventRequest(
    string Title,
    string? Summary,
    string? Description,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    LocationDto? Location,
    string? ImageUrl,
    EventVisibility Visibility,
    bool RegistrationRequired,
    DateTimeOffset? RegistrationOpensAt,
    DateTimeOffset? RegistrationClosesAt,
    int? Capacity,
    bool WaitlistEnabled,
    int MaxPerRegistration,
    IReadOnlyList<QuestionInput> Questions,
    string? Scope);

public sealed record TicketDto(Guid AttendeeId, string Name, string Code, DateTimeOffset? CheckedInAt);

public sealed record RegistrationDto(
    Guid Id,
    Guid EventId,
    string EventTitle,
    string EventSlug,
    DateTimeOffset StartsAt,
    LocationDto? Location,
    RegistrationStatus Status,
    int? WaitlistPosition,
    IReadOnlyList<TicketDto> Tickets,
    DateTimeOffset CreatedAt);

/// <summary>Returned once to a guest: the key lets them view or cancel later and is never stored in plain text.</summary>
public sealed record GuestRegistrationReceipt(RegistrationDto Registration, string AccessKey);

public sealed record MemberRegisterRequest(IReadOnlyList<Guid> AttendeePersonIds, IReadOnlyDictionary<Guid, string>? Answers);

public sealed record GuestCodeRequest(string Email);

public sealed record GuestCodeResponse(Guid VerificationId, DateTimeOffset ExpiresAt);

public sealed record GuestRegisterRequest(
    Guid VerificationId,
    string Code,
    string FirstName,
    string LastName,
    string Email,
    string? Mobile,
    IReadOnlyList<string>? OtherAttendeeNames,
    IReadOnlyDictionary<Guid, string>? Answers,
    bool ConsentToKeepDetails,
    string? PolicyVersion);

public sealed record AdminRegisterRequest(
    Guid? PersonId,
    string? FirstName,
    string? LastName,
    string? Mobile,
    string? Email,
    bool ConsentGivenVerbally,
    IReadOnlyList<string>? OtherAttendeeNames,
    IReadOnlyDictionary<Guid, string>? Answers);

public sealed record AttendeeRowDto(
    Guid RegistrationId,
    Guid AttendeeId,
    string Name,
    Guid? PersonId,
    string RegistrantName,
    string? RegistrantEmail,
    RegistrationStatus Status,
    RegistrationSource Source,
    DateTimeOffset RegisteredAt,
    DateTimeOffset? CheckedInAt,
    IReadOnlyDictionary<string, string> Answers);

public sealed record CheckInRequest(string? TicketCode, Guid? AttendeeId);

public sealed record CheckInResultDto(CheckInOutcome Outcome, string Name, DateTimeOffset? CheckedInAt, string Message);
