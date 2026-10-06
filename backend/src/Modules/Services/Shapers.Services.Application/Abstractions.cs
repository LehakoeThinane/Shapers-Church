using Microsoft.EntityFrameworkCore;
using Shapers.Platform.Authorization;
using Shapers.Services.Contracts;
using Shapers.Services.Domain;

namespace Shapers.Services.Application;

public interface IServicesDb
{
    DbSet<TeamCategory> Categories { get; }

    DbSet<Team> Teams { get; }

    DbSet<TeamPosition> Positions { get; }

    DbSet<TeamMember> Members { get; }

    DbSet<Blockout> Blockouts { get; }

    DbSet<Assignment> Assignments { get; }

    DbSet<ServiceType> ServiceTypes { get; }

    DbSet<Plan> Plans { get; }

    DbSet<Song> Songs { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Signs the links in serving emails so people can answer without signing in.</summary>
public interface IAnswerLinks
{
    string Token(Guid assignmentId);

    /// <summary>The assignment, if the token is genuine and not expired.</summary>
    Guid? Read(string token);
}

public sealed class ServicesOptions
{
    public const string SectionName = "Services";

    /// <summary>The API's public address, for answer links in emails. Defaults to Communications:PublicApiUrl.</summary>
    public string? PublicApiUrl { get; set; }

    /// <summary>How many days before a service people who said yes get a reminder.</summary>
    public int ReminderDaysBefore { get; set; } = 3;
}

public sealed class ServicesPermissionProvider : IPermissionProvider
{
    public IEnumerable<PermissionDefinition> GetPermissions() =>
    [
        new(ServicesPermissions.PlansEdit, "services", "Plan services: orders of service, templates, the live run sheet"),
        new(ServicesPermissions.Schedule, "services", "Manage serving teams and schedule people"),
        new(ServicesPermissions.SongsEdit, "services", "Keep the song library: arrangements, keys, charts and lyrics"),
        new(ServicesPermissions.CategoriesManage, "services", "Manage the categories teams are grouped in (Ministries, Disciplines, Departments)"),
    ];
}

// ---------- Teams ----------

public sealed record SaveTeamRequest(string Name, string? Description, bool OpenToMinors, string? Scope, Guid? CategoryId = null);

public sealed record SaveCategoryRequest(string Name, string? Description, int Order);

public sealed record CategoryDto(Guid Id, string Name, string? Description, int Order);

public sealed record SavePositionRequest(string Name, int Order);

public sealed record SaveMemberRequest(Guid PersonId, IReadOnlyList<Guid> PositionIds, bool IsLeader);

public sealed record PositionDto(Guid Id, string Name, int Order);

public sealed record TeamMemberDto(Guid PersonId, string Name, IReadOnlyList<Guid> PositionIds, bool IsLeader);

public sealed record TeamDto(Guid Id, string Name, string? Description, bool OpenToMinors, string Scope, bool IsArchived, IReadOnlyList<PositionDto> Positions, IReadOnlyList<TeamMemberDto> Members, Guid? CategoryId);

// ---------- Plans ----------

public sealed record SaveServiceTypeRequest(string Name, TimeOnly StartTime, IReadOnlyList<PlanItem> Items, IReadOnlyList<PositionNeed> Needs, string? Scope);

public sealed record ServiceTypeDto(Guid Id, string Name, TimeOnly StartTime, string Scope, IReadOnlyList<PlanItem> Items, IReadOnlyList<PositionNeed> Needs);

public sealed record CreatePlanRequest(Guid? ServiceTypeId, DateOnly Date, string? Title, TimeOnly? StartTime, string? Scope);

public sealed record SavePlanDetailsRequest(string Title, DateOnly Date, TimeOnly StartTime, string? SeriesTitle, string? Notes, Guid? LivestreamId);

public sealed record SavePlanItemsRequest(IReadOnlyList<PlanItem> Items);

public sealed record SavePlanNeedsRequest(IReadOnlyList<PositionNeed> Needs);

public sealed record PlanItemDto(PlanItem Item, TimeOnly StartsAt, string? SongTitle, string? ArrangementName);

public sealed record AssignmentDto(
    Guid Id,
    Guid PlanId,
    Guid TeamId,
    string Team,
    Guid PositionId,
    string Position,
    Guid PersonId,
    string Name,
    AssignmentStatus Status,
    string? DeclineReason,
    DateTimeOffset RequestedAt,
    DateTimeOffset? RespondedAt);

public sealed record NeedDto(Guid TeamId, string Team, Guid PositionId, string Position, int Needed, int Filled, int Pending);

public sealed record PlanSummaryDto(Guid Id, string Title, DateOnly Date, TimeOnly StartTime, string? SeriesTitle, int Needed, int Accepted, int Pending, int Declined, bool IsLive);

public sealed record PlanDto(
    Guid Id,
    Guid? ServiceTypeId,
    string Title,
    DateOnly Date,
    TimeOnly StartTime,
    TimeOnly EndTime,
    string Scope,
    string? SeriesTitle,
    string? Notes,
    Guid? LivestreamId,
    IReadOnlyList<PlanItemDto> Items,
    IReadOnlyList<NeedDto> Needs,
    IReadOnlyList<AssignmentDto> Assignments,
    Guid? LiveItemId,
    DateTimeOffset UpdatedAt);

// ---------- Scheduling ----------

public sealed record AssignRequest(Guid PositionId, Guid PersonId);

public sealed record CandidateDto(Guid PersonId, string Name, bool Available, string? Reason, DateOnly? LastServed);

public sealed record MatrixCellDto(Guid PlanId, Guid PositionId, IReadOnlyList<MatrixPersonDto> People, int Needed);

public sealed record MatrixPersonDto(Guid AssignmentId, string Name, AssignmentStatus Status);

public sealed record MatrixRowDto(Guid TeamId, string Team, Guid PositionId, string Position);

public sealed record MatrixDto(IReadOnlyList<PlanSummaryDto> Plans, IReadOnlyList<MatrixRowDto> Rows, IReadOnlyList<MatrixCellDto> Cells);

// ---------- Songs ----------

public sealed record SaveSongRequest(string Title, string? Author, string? CcliNumber, IReadOnlyList<string> Themes, string? Lyrics, string? ReferenceUrl, IReadOnlyList<Arrangement> Arrangements);

public sealed record SongSummaryDto(Guid Id, string Title, string? Author, string? CcliNumber, IReadOnlyList<string> Themes, IReadOnlyList<string> Keys, DateOnly? LastUsed, int TimesUsed, bool IsArchived);

public sealed record SongDto(
    Guid Id,
    string Title,
    string? Author,
    string? CcliNumber,
    IReadOnlyList<string> Themes,
    string? Lyrics,
    string? ReferenceUrl,
    IReadOnlyList<Arrangement> Arrangements,
    DateOnly? LastUsed,
    int TimesUsed,
    bool IsArchived);

public sealed record SongUsageDto(Guid SongId, string Title, string? Author, string? CcliNumber, int Times, IReadOnlyList<DateOnly> Dates);

// ---------- Live ----------

public sealed record LiveDto(
    Guid PlanId,
    string Title,
    DateOnly Date,
    bool IsLive,
    Guid? CurrentItemId,
    DateTimeOffset? CurrentStartedAt,
    DateTimeOffset ServerTime,
    IReadOnlyList<PlanItemDto> Items);

public sealed record GoLiveRequest(Guid? ItemId);

// ---------- Members ----------

public sealed record MyAssignmentDto(
    Guid Id,
    Guid PlanId,
    string PlanTitle,
    DateOnly Date,
    TimeOnly StartTime,
    string Team,
    string Position,
    AssignmentStatus Status);

public sealed record ServingAnswerRequest(bool Accept, string? Reason);

public sealed record SaveBlockoutRequest(DateOnly From, DateOnly To, string? Reason);

public sealed record BlockoutDto(Guid Id, DateOnly From, DateOnly To, string? Reason);

/// <summary>A song as the band rehearses it: the arrangement chosen for this plan, with chart, recording and lyrics.</summary>
public sealed record RehearseSongDto(Guid ItemId, Guid SongId, string Title, string? Author, string? Key, int? Bpm, string? ChartUrl, string? AudioUrl, string? ReferenceUrl, string? Lyrics, string? Notes);

public sealed record RehearsePlanDto(
    Guid Id,
    string Title,
    DateOnly Date,
    TimeOnly StartTime,
    string? Notes,
    IReadOnlyList<PlanItemDto> Items,
    IReadOnlyList<RehearseSongDto> Songs,
    IReadOnlyList<AssignmentDto> Team);
