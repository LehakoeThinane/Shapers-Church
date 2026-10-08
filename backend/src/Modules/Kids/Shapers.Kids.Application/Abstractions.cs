using Microsoft.EntityFrameworkCore;
using Shapers.Kids.Contracts;
using Shapers.Kids.Domain;
using Shapers.Platform.Authorization;

namespace Shapers.Kids.Application;

public interface IKidsDb
{
    DbSet<KidsClass> Classes { get; }

    DbSet<CareNote> CareNotes { get; }

    DbSet<CheckIn> CheckIns { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public sealed class KidsPermissionProvider : IPermissionProvider
{
    public IEnumerable<PermissionDefinition> GetPermissions() =>
    [
        new(KidsPermissions.CheckIn, "kids", "Run kids check-in: see who is in each class today, check children in at the desk and hand them back"),
        new(KidsPermissions.CareView, "kids", "Read children's care notes: allergies and medical needs (reads are audited)", IsSensitive: true),
        new(KidsPermissions.Manage, "kids", "Set up the kids classes and their age ranges"),
    ];
}

public sealed record KidsClassDto(Guid Id, string Name, int FromAge, int ToAge, bool IsArchived);

public sealed record SaveKidsClassRequest(string Name, int FromAge, int ToAge);

public sealed record CareNotesDto(string? Allergies, string? Medical, string? Other);

/// <summary>Today's check-in for a parent's child, with the pickup code the parent shows to collect them.</summary>
public sealed record MyChildCheckInDto(Guid Id, string ClassName, string PickupCode, DateTimeOffset CheckedInAt, DateTimeOffset? CollectedAt);

public sealed record MyChildDto(Guid PersonId, string FirstName, string Name, DateOnly? DateOfBirth, int? Age, string? ClassName, CareNotesDto? CareNotes, MyChildCheckInDto? Today);

/// <summary>A parent adds their child. <see cref="GuardianConsent"/>: they are the parent or guardian and agree to the church keeping these details.</summary>
public sealed record AddChildRequest(string FirstName, string LastName, DateOnly DateOfBirth, string? Allergies, string? Medical, string? Other, bool GuardianConsent);

public sealed record CareNotesRequest(string? Allergies, string? Medical, string? Other);

public sealed record ParentCheckInRequest(IReadOnlyList<Guid> ChildIds);

/// <summary>A child in a class today, as the kids team sees them. Never the care notes themselves; never the pickup code.</summary>
public sealed record CheckedInChildDto(Guid CheckInId, Guid ChildId, string Name, int? Age, bool HasCareNotes, DateTimeOffset CheckedInAt, CheckInMethod Method, DateTimeOffset? CollectedAt);

public sealed record ClassTodayDto(Guid ClassId, string Name, int FromAge, int ToAge, IReadOnlyList<CheckedInChildDto> Children);

public sealed record KidsTodayDto(DateOnly Date, IReadOnlyList<ClassTodayDto> Classes);

public sealed record PickupChildDto(Guid CheckInId, string Name, string ClassName, DateTimeOffset CheckedInAt);

public sealed record PickupDto(string Code, IReadOnlyList<PickupChildDto> Children);

public sealed record CheckOutRequest(string Code, IReadOnlyList<Guid> CheckInIds);

public sealed record DeskChildRequest(string FirstName, string LastName, DateOnly DateOfBirth, string? Allergies, string? Medical, string? Other);

/// <summary>A visiting family at the desk. <see cref="Consent"/>: the parent agreed to the church keeping their and their children's details.</summary>
public sealed record DeskCheckInRequest(string ParentFirstName, string ParentLastName, string ParentMobile, bool Consent, IReadOnlyList<DeskChildRequest> Children);

/// <summary>What a name label shows: enough to match child and parent, and a flag that the team must read the care notes.</summary>
public sealed record KidsLabelDto(Guid CheckInId, string ChildName, string ClassName, DateOnly Date, string PickupCode, bool HasCareNotes);

public sealed record DeskCheckInResultDto(string PickupCode, IReadOnlyList<KidsLabelDto> Labels);
