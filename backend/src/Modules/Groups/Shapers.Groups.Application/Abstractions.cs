using Microsoft.EntityFrameworkCore;
using Shapers.Groups.Contracts;
using Shapers.Groups.Domain;
using Shapers.Platform.Authorization;

namespace Shapers.Groups.Application;

public interface IGroupsDb
{
    DbSet<Cell> Cells { get; }

    DbSet<CellMember> Members { get; }

    DbSet<CellReport> Reports { get; }

    DbSet<CellMaterial> Materials { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public sealed class GroupsPermissionProvider : IPermissionProvider
{
    public IEnumerable<PermissionDefinition> GetPermissions() =>
    [
        new(GroupsPermissions.CellsManage, "groups", "Create and close cells, and choose their leaders and members", IsSensitive: true),
        new(GroupsPermissions.ReportsView, "groups", "Read cell reports and leaders' teaching materials (reads are audited)", IsSensitive: true),
    ];
}

public sealed class GroupsOptions
{
    /// <summary>Cell leaders need a two-step sign-in to open their cell: reports hold special personal information.</summary>
    public bool RequireMfaForLeaders { get; set; } = true;
}

public sealed record SaveCellRequest(string Name, Guid CampusId, DayOfWeek? MeetingDay, TimeOnly? MeetingTime, string? Area, string? Address);

public sealed record AddMemberRequest(Guid PersonId, CellRole Role);

/// <summary>A leader adding someone new: creates a church record with their consent (flagged for duplicate review).</summary>
public sealed record NewMemberRequest(string FirstName, string LastName, string? Mobile, string? Email, bool AgreedToBeOnRecord);

public sealed record CellMemberDto(Guid PersonId, string Name, CellRole Role, DateTimeOffset JoinedAt);

/// <summary>A cell at a glance, for the pastors' overview.</summary>
public sealed record CellSummaryDto(
    Guid Id,
    string Name,
    Guid CampusId,
    DayOfWeek? MeetingDay,
    TimeOnly? MeetingTime,
    string? Area,
    CellStatus Status,
    IReadOnlyList<string> Leaders,
    int MemberCount,
    DateOnly? LastReportDate,
    int? LastAttendance,
    bool ReportedThisWeek,
    int OpenUrgentFollowUps);

public sealed record CellDetailDto(
    Guid Id,
    string Name,
    Guid CampusId,
    DayOfWeek? MeetingDay,
    TimeOnly? MeetingTime,
    string? Area,
    string? Address,
    CellStatus Status,
    IReadOnlyList<CellMemberDto> Members);

public sealed record SaveReportRequest(
    DateOnly MeetingDate,
    string? Topic,
    Guid? MaterialId,
    string? Notes,
    string? Highlights,
    string? PrayerNeeds,
    MultiplicationReadiness Multiplication,
    IReadOnlyList<Guid> AttendeeIds,
    IReadOnlyList<ReportVisitor> Visitors,
    IReadOnlyList<ReportFollowUp> FollowUps,
    IReadOnlyList<ReportGrowth> Growth,
    bool Submit);

public sealed record ReportSummaryDto(
    Guid Id,
    Guid CellId,
    string CellName,
    DateOnly MeetingDate,
    ReportStatus Status,
    string? Topic,
    int MembersPresent,
    int VisitorCount,
    int OpenUrgentFollowUps,
    string WrittenBy,
    DateTimeOffset? SubmittedAt,
    bool Redacted);

public sealed record PersonRefDto(Guid PersonId, string Name);

public sealed record GrowthDto(Guid PersonId, string Name, NextStep Step);

public sealed record ReportDto(
    Guid Id,
    Guid CellId,
    string CellName,
    DateOnly MeetingDate,
    ReportStatus Status,
    string? Topic,
    Guid? MaterialId,
    string? MaterialTitle,
    string? Notes,
    string? Highlights,
    string? PrayerNeeds,
    MultiplicationReadiness Multiplication,
    IReadOnlyList<PersonRefDto> Attendees,
    IReadOnlyList<ReportVisitor> Visitors,
    IReadOnlyList<ReportFollowUp> FollowUps,
    IReadOnlyList<GrowthDto> Growth,
    int MembersPresent,
    int VisitorCount,
    string WrittenBy,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? SubmittedAt,
    bool Redacted);

public sealed record SaveMaterialRequest(string Title, string Body, string? Link, DateOnly? ForDate, bool SharedWithMembers);

public sealed record MaterialDto(
    Guid Id,
    Guid CellId,
    string CellName,
    string Title,
    string Body,
    string? Link,
    DateOnly? ForDate,
    bool SharedWithMembers,
    string WrittenBy,
    DateTimeOffset UpdatedAt);

/// <summary>A cell as one of its members or leaders sees it.</summary>
public sealed record MyCellDto(
    Guid Id,
    string Name,
    DayOfWeek? MeetingDay,
    TimeOnly? MeetingTime,
    string? Area,
    string? Address,
    CellRole MyRole,
    IReadOnlyList<string> Leaders,
    IReadOnlyList<MaterialDto> SharedMaterials);
