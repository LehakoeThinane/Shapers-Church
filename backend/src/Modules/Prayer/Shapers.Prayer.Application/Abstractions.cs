using Microsoft.EntityFrameworkCore;
using Shapers.Platform.Authorization;
using Shapers.Prayer.Contracts;
using Shapers.Prayer.Domain;

namespace Shapers.Prayer.Application;

public interface IPrayerDb
{
    DbSet<PrayerRequest> Requests { get; }

    DbSet<PrayerResponse> Responses { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public sealed class PrayerPermissionProvider : IPermissionProvider
{
    public IEnumerable<PermissionDefinition> GetPermissions() =>
    [
        new(PrayerPermissions.RequestsView, "prayer", "See all prayer requests, including pastors-only ones", IsSensitive: true),
        new(PrayerPermissions.RequestsModerate, "prayer", "Review requests for the prayer wall", IsSensitive: true),
    ];
}

public sealed record SubmitPrayerRequest(string Text, bool ShareOnWall, bool Anonymous, bool Consent);

public sealed record AnswerRequest(string? Note);

public sealed record ReviewRequest(string? WallText, string? Note);

/// <summary>A request as the wall shows it: first name or "Someone from Shapers", and the reviewed wording.</summary>
public sealed record PrayerWallItemDto(Guid Id, string Name, string Text, DateTimeOffset SharedAt, int PrayedCount, bool IPrayed, bool IsMine, bool Answered);

public sealed record MyPrayerRequestDto(
    Guid Id,
    string Text,
    string? WallText,
    PrayerVisibility Visibility,
    bool Anonymous,
    PrayerStatus Status,
    string? ReviewNote,
    int PrayedCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? WallUntil,
    DateTimeOffset? AnsweredAt,
    string? AnswerNote);

/// <summary>What pastors and reviewers see: who asked, the original words, and what happened.</summary>
public sealed record PrayerAdminDto(
    Guid Id,
    Guid PersonId,
    string PersonName,
    string Text,
    string? WallText,
    PrayerVisibility Visibility,
    bool Anonymous,
    PrayerStatus Status,
    PrayerSource Source,
    string Scope,
    int PrayedCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReviewedAt,
    string? ReviewNote,
    DateTimeOffset? AnsweredAt,
    string? AnswerNote);
