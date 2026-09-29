using Microsoft.EntityFrameworkCore;
using Shapers.Church.Contracts;
using Shapers.Media.Contracts;
using Shapers.Media.Domain;
using Shapers.Platform.Auditing;
using Shapers.Platform.Authorization;

namespace Shapers.Media.Application;

public sealed record CueDto(Guid Id, string Reference, string? Text, int Order, DateTimeOffset? ShownAt);

public sealed record LivestreamAdminDto(
    Guid Id,
    string Title,
    string Scope,
    DateTimeOffset ScheduledStart,
    LivestreamStatus Status,
    VideoDto? Video,
    string? Notes,
    string? GiveUrl,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    Guid? CurrentCueId,
    Guid? SermonId,
    IReadOnlyList<CueDto> Cues);

public sealed record SaveLivestreamRequest(string Title, DateTimeOffset ScheduledStart, string? VideoUrl, string? Notes, string? GiveUrl, string? Scope);

public sealed record AddCueRequest(string Reference, string? Text);

public sealed record ShowCueRequest(Guid? CueId);

public enum LiveState
{
    None,
    Upcoming,
    Live,
}

/// <summary>What the Live tab needs, kept small because apps poll it every few seconds during a service.</summary>
public sealed record PublicLivestreamDto(
    Guid Id,
    string Title,
    LivestreamStatus Status,
    DateTimeOffset ScheduledStart,
    DateTimeOffset? StartedAt,
    string? YouTubeId,
    string? Notes,
    string? GiveUrl,
    CueDto? OnScreen,
    IReadOnlyList<CueDto> Shown);

public sealed record LiveNowDto(LiveState State, PublicLivestreamDto? Stream);

public sealed class LivestreamService(
    IMediaDb db,
    IAuthorizer authorizer,
    IChurchDirectory church,
    IAuditLog audit,
    TimeProvider clock)
{
    private static readonly Error NotFound = Error.NotFound("media.stream_not_found", "Livestream not found.");
    private static readonly TimeZoneInfo Johannesburg = TimeZoneInfo.FindSystemTimeZoneById("Africa/Johannesburg");

    public async Task<IReadOnlyList<LivestreamAdminDto>> ListAsync(CancellationToken cancellationToken)
    {
        var scopes = await authorizer.ScopesForAsync(MediaPermissions.LivestreamManage, cancellationToken);
        var since = clock.GetUtcNow().AddDays(-60);
        var streams = await db.Livestreams.AsNoTracking()
            .WithinScopes(s => s.Scope, scopes)
            .Where(s => s.ScheduledStart >= since || s.Status == LivestreamStatus.Live)
            .OrderByDescending(s => s.ScheduledStart)
            .Take(50)
            .ToListAsync(cancellationToken);
        return streams.Select(ToAdminDto).ToList();
    }

    public async Task<Result<LivestreamAdminDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var stream = await db.Livestreams.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        return stream is null || !await CanAsync(stream, cancellationToken) ? NotFound : ToAdminDto(stream);
    }

    public async Task<Result<LivestreamAdminDto>> CreateAsync(SaveLivestreamRequest request, CancellationToken cancellationToken)
    {
        var scope = await ResolveScopeAsync(request.Scope, cancellationToken);
        if (scope is null)
        {
            return new Error("media.scope_invalid", "Choose the church or a campus.");
        }

        if (!await authorizer.CanAsync(MediaPermissions.LivestreamManage, scope, cancellationToken))
        {
            return Error.Forbidden("media.forbidden", "You can't schedule streams here.");
        }

        var video = ParseVideo(request.VideoUrl);
        if (video.IsFailure)
        {
            return video.Error!;
        }

        var now = clock.GetUtcNow();
        var stream = Livestream.Schedule(request.Title, scope, request.ScheduledStart, now);
        stream.Update(request.Title, request.ScheduledStart, video.Value, request.Notes, request.GiveUrl, now);
        db.Livestreams.Add(stream);
        await db.SaveChangesAsync(cancellationToken);
        return ToAdminDto(stream);
    }

    public Task<Result<LivestreamAdminDto>> UpdateAsync(Guid id, SaveLivestreamRequest request, CancellationToken cancellationToken) =>
        ChangeAsync(id, null, stream =>
        {
            var video = ParseVideo(request.VideoUrl);
            if (video.IsFailure)
            {
                return video;
            }

            stream.Update(request.Title, request.ScheduledStart, video.Value, request.Notes, request.GiveUrl, clock.GetUtcNow());
            return Result.Success();
        }, cancellationToken);

    public Task<Result<LivestreamAdminDto>> GoLiveAsync(Guid id, CancellationToken cancellationToken) =>
        ChangeAsync(id, "media.livestream.started", stream =>
        {
            stream.GoLive(clock.GetUtcNow());
            return Result.Success();
        }, cancellationToken);

    public Task<Result<LivestreamAdminDto>> EndAsync(Guid id, CancellationToken cancellationToken) =>
        ChangeAsync(id, "media.livestream.ended", stream =>
        {
            stream.End(clock.GetUtcNow());
            return Result.Success();
        }, cancellationToken);

    public Task<Result<LivestreamAdminDto>> CancelAsync(Guid id, CancellationToken cancellationToken) =>
        ChangeAsync(id, "media.livestream.cancelled", stream =>
        {
            stream.Cancel(clock.GetUtcNow());
            return Result.Success();
        }, cancellationToken);

    public Task<Result<LivestreamAdminDto>> AddCueAsync(Guid id, AddCueRequest request, CancellationToken cancellationToken) =>
        ChangeAsync(id, null, stream =>
        {
            if (!ScriptureReference.TryParse(request.Reference, out var reference))
            {
                return new Error("media.scripture_invalid", $"We couldn't read \"{request.Reference}\". Try a form like \"John 3:16\".");
            }

            stream.AddCue(reference, request.Text, clock.GetUtcNow());
            return Result.Success();
        }, cancellationToken);

    public Task<Result<LivestreamAdminDto>> RemoveCueAsync(Guid id, Guid cueId, CancellationToken cancellationToken) =>
        ChangeAsync(id, null, stream =>
        {
            stream.RemoveCue(cueId, clock.GetUtcNow());
            return Result.Success();
        }, cancellationToken);

    public Task<Result<LivestreamAdminDto>> ShowCueAsync(Guid id, ShowCueRequest request, CancellationToken cancellationToken) =>
        ChangeAsync(id, null, stream =>
        {
            stream.ShowCue(request.CueId, clock.GetUtcNow());
            return Result.Success();
        }, cancellationToken);

    /// <summary>
    /// Turns a finished service into a draft sermon with the video, notes and scripture already filled in.
    /// Needs sermon-editing rights as well, since the result is a sermon.
    /// </summary>
    public async Task<Result<Guid>> MakeSermonAsync(Guid id, CancellationToken cancellationToken)
    {
        var stream = await db.Livestreams.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (stream is null || !await CanAsync(stream, cancellationToken))
        {
            return NotFound;
        }

        var scope = ScopePath.Parse(stream.Scope);
        if (!await authorizer.CanAsync(MediaPermissions.SermonsEdit, scope, cancellationToken))
        {
            return Error.Forbidden("media.forbidden", "You need sermon-editing rights to make a sermon.");
        }

        var now = clock.GetUtcNow();
        var preachedOn = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(stream.StartedAt ?? stream.ScheduledStart, Johannesburg).DateTime);
        var sermon = Sermon.Create(stream.Title, preachedOn, scope, now);
        var slug = sermon.Slug;
        for (var n = 2; await db.Sermons.AnyAsync(s => s.Slug == slug, cancellationToken); n++)
        {
            slug = $"{sermon.Slug}-{n}";
        }

        sermon.UseSlug(slug);
        sermon.UpdateDetails(stream.Title, preachedOn, null, null, stream.Notes, [], now);
        sermon.SetVideo(stream.Video, now);
        sermon.SetScripture(
            stream.Cues.OrderBy(c => c.Order)
                .Select(c => ScriptureReference.TryParse(c.Reference.Replace('–', '-'), out var r) ? r : null)
                .OfType<ScriptureReference>(),
            now);
        sermon.RefreshSearchText([], null);
        stream.LinkSermon(sermon.Id, now);
        db.Sermons.Add(sermon);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord("media.livestream.sermon_made", "livestream", stream.Id.ToString(), scope, new { sermonId = sermon.Id }), cancellationToken);
        return sermon.Id;
    }

    /// <summary>
    /// What's on now: the live stream if there is one; otherwise the next one within a week; otherwise nothing.
    /// </summary>
    public async Task<LiveNowDto> NowAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var live = await db.Livestreams.AsNoTracking()
            .Where(s => s.Status == LivestreamStatus.Live)
            .OrderByDescending(s => s.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (live is not null)
        {
            return new LiveNowDto(LiveState.Live, ToPublicDto(live));
        }

        var weekAhead = now.AddDays(7);
        var next = await db.Livestreams.AsNoTracking()
            .Where(s => s.Status == LivestreamStatus.Scheduled && s.ScheduledStart >= now.AddHours(-2) && s.ScheduledStart <= weekAhead)
            .OrderBy(s => s.ScheduledStart)
            .FirstOrDefaultAsync(cancellationToken);
        return next is null ? new LiveNowDto(LiveState.None, null) : new LiveNowDto(LiveState.Upcoming, ToPublicDto(next));
    }

    private async Task<Result<LivestreamAdminDto>> ChangeAsync(Guid id, string? auditAction, Func<Livestream, Result> change, CancellationToken cancellationToken)
    {
        var stream = await db.Livestreams.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (stream is null || !await CanAsync(stream, cancellationToken))
        {
            return NotFound;
        }

        var result = change(stream);
        if (result.IsFailure)
        {
            return result.Error!;
        }

        await db.SaveChangesAsync(cancellationToken);
        if (auditAction is not null)
        {
            await audit.RecordAsync(new AuditRecord(auditAction, "livestream", stream.Id.ToString(), ScopePath.Parse(stream.Scope), new { stream.Title }), cancellationToken);
        }

        return ToAdminDto(stream);
    }

    private async Task<ScopePath?> ResolveScopeAsync(string? requested, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(requested))
        {
            return ScopePath.Parse((await church.GetRootScopeAsync(cancellationToken)).Path);
        }

        return ScopePath.TryParse(requested, out var scope) && await church.ScopeExistsAsync(scope.Value, cancellationToken) ? scope : null;
    }

    private static Result<VideoLink?> ParseVideo(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return Result<VideoLink?>.Ok(null);
        }

        return VideoLink.TryParseYouTube(url, out var video)
            ? Result<VideoLink?>.Ok(video)
            : new Error("media.video_invalid", "Paste the YouTube Live link, e.g. https://www.youtube.com/live/…");
    }

    private Task<bool> CanAsync(Livestream stream, CancellationToken cancellationToken) =>
        authorizer.CanAsync(MediaPermissions.LivestreamManage, ScopePath.Parse(stream.Scope), cancellationToken);

    private static CueDto ToDto(ScriptureCue c) => new(c.Id, c.Reference, c.Text, c.Order, c.ShownAt);

    private static LivestreamAdminDto ToAdminDto(Livestream s) =>
        new(
            s.Id,
            s.Title,
            s.Scope,
            s.ScheduledStart,
            s.Status,
            s.Video is { } v ? new VideoDto(v.Provider, v.ExternalId, v.WatchUrl, v.ThumbnailUrl) : null,
            s.Notes,
            s.GiveUrl,
            s.StartedAt,
            s.EndedAt,
            s.CurrentCueId,
            s.SermonId,
            s.Cues.OrderBy(c => c.Order).Select(ToDto).ToList());

    private static PublicLivestreamDto ToPublicDto(Livestream s) =>
        new(
            s.Id,
            s.Title,
            s.Status,
            s.ScheduledStart,
            s.StartedAt,
            s.Video?.ExternalId,
            s.Notes,
            s.GiveUrl,
            s.CurrentCue is { } cue ? ToDto(cue) : null,
            s.Cues.Where(c => c.ShownAt is not null).OrderBy(c => c.ShownAt).Select(ToDto).ToList());
}
