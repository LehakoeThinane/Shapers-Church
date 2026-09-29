using Microsoft.EntityFrameworkCore;
using Shapers.Church.Contracts;
using Shapers.Media.Contracts;
using Shapers.Media.Domain;
using Shapers.Platform.Auditing;
using Shapers.Platform.Authorization;
using Shapers.Platform.Text;
using Shapers.Platform.Web;

namespace Shapers.Media.Application;

/// <summary>Staff use cases for sermons. Editing needs media.sermons.edit, going public needs media.sermons.publish, both at the sermon's scope.</summary>
public sealed class SermonAdminService(
    IMediaDb db,
    SermonReader reader,
    IAuthorizer authorizer,
    IChurchDirectory church,
    IAuditLog audit,
    TimeProvider clock)
{
    private static readonly Error NotFound = Error.NotFound("media.sermon_not_found", "Sermon not found.");

    public async Task<PagedResult<SermonAdminListItemDto>> ListAsync(AdminSermonQuery request, CancellationToken cancellationToken)
    {
        var paging = new PageRequest(request.Page, request.PageSize);
        var scopes = await authorizer.ScopesForAsync(MediaPermissions.SermonsEdit, cancellationToken);
        var query = db.Sermons.AsNoTracking().WithinScopes(s => s.Scope, scopes);

        if (request.Status is { } status)
        {
            query = query.Where(s => s.Status == status);
        }

        if (request.SeriesId is { } seriesId)
        {
            query = query.Where(s => s.SeriesId == seriesId);
        }

        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var pattern = LikePattern.Contains(request.Q.Trim());
            query = query.Where(s => EF.Functions.ILike(s.SearchText, pattern) || EF.Functions.ILike(s.Title, pattern));
        }

        var total = await query.CountAsync(cancellationToken);
        var sermons = await query
            .OrderByDescending(s => s.PreachedOn).ThenByDescending(s => s.CreatedAt)
            .Skip(paging.Skip).Take(paging.SafePageSize)
            .ToListAsync(cancellationToken);

        var (speakers, series, _) = await reader.LoadRelatedAsync(sermons, cancellationToken);
        var items = sermons.Select(s => new SermonAdminListItemDto(
            s.Id,
            s.Title,
            s.PreachedOn,
            s.Status,
            s.PublishAt,
            s.Speakers.OrderBy(x => x.Order).Select(x => speakers.GetValueOrDefault(x.SpeakerId)?.Name).OfType<string>().ToList(),
            s.SeriesId is { } id ? series.GetValueOrDefault(id)?.Title : null,
            s.AudioAssetId is not null,
            s.Video is not null)).ToList();
        return new PagedResult<SermonAdminListItemDto>(items, paging.SafePage, paging.SafePageSize, total);
    }

    public async Task<Result<SermonAdminDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var sermon = await db.Sermons.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        return sermon is null || !await CanAsync(MediaPermissions.SermonsEdit, sermon, cancellationToken)
            ? NotFound
            : await ToAdminDtoAsync(sermon, cancellationToken);
    }

    public async Task<Result<SermonAdminDto>> CreateAsync(SaveSermonRequest request, CancellationToken cancellationToken)
    {
        ScopePath scope;
        if (string.IsNullOrWhiteSpace(request.Scope))
        {
            scope = ScopePath.Parse((await church.GetRootScopeAsync(cancellationToken)).Path);
        }
        else if (!ScopePath.TryParse(request.Scope, out scope) || !await church.ScopeExistsAsync(scope.Value, cancellationToken))
        {
            return new Error("media.scope_invalid", "Choose the church or a campus.");
        }

        if (!await authorizer.CanAsync(MediaPermissions.SermonsEdit, scope, cancellationToken))
        {
            return Error.Forbidden("media.forbidden", "You can't add sermons here.");
        }

        var now = clock.GetUtcNow();
        var sermon = Sermon.Create(request.Title, request.PreachedOn, scope, now);
        sermon.UseSlug(await UniqueSlugAsync(sermon.Slug, cancellationToken));
        var applied = await ApplyAsync(sermon, request, now, cancellationToken);
        if (applied.IsFailure)
        {
            return applied.Error!;
        }

        db.Sermons.Add(sermon);
        await db.SaveChangesAsync(cancellationToken);
        await AuditAsync("media.sermon.created", sermon, cancellationToken);
        return await ToAdminDtoAsync(sermon, cancellationToken);
    }

    public Task<Result<SermonAdminDto>> UpdateAsync(Guid id, SaveSermonRequest request, CancellationToken cancellationToken) =>
        ChangeAsync(id, MediaPermissions.SermonsEdit, "media.sermon.updated", (sermon, now) => ApplyAsync(sermon, request, now, cancellationToken), cancellationToken);

    public Task<Result<SermonAdminDto>> SetAudioAsync(Guid id, SetSermonAssetRequest request, CancellationToken cancellationToken) =>
        ChangeAsync(id, MediaPermissions.SermonsEdit, "media.sermon.audio_changed", async (sermon, now) =>
        {
            if (request.AssetId is not { } assetId)
            {
                sermon.RemoveAudio(now);
                return Result.Success();
            }

            var asset = await db.Assets.SingleOrDefaultAsync(a => a.Id == assetId, cancellationToken);
            if (asset is null)
            {
                return Error.NotFound("media.asset_not_found", "Upload not found.");
            }

            sermon.AttachAudio(asset, now);
            return Result.Success();
        }, cancellationToken);

    public Task<Result<SermonAdminDto>> SetNotesPdfAsync(Guid id, SetSermonAssetRequest request, CancellationToken cancellationToken) =>
        ChangeAsync(id, MediaPermissions.SermonsEdit, "media.sermon.pdf_changed", async (sermon, now) =>
        {
            var asset = request.AssetId is { } assetId ? await db.Assets.SingleOrDefaultAsync(a => a.Id == assetId, cancellationToken) : null;
            if (request.AssetId is not null && asset is null)
            {
                return Error.NotFound("media.asset_not_found", "Upload not found.");
            }

            sermon.AttachNotesPdf(asset, now);
            return Result.Success();
        }, cancellationToken);

    public Task<Result<SermonAdminDto>> PublishAsync(Guid id, CancellationToken cancellationToken) =>
        ChangeAsync(id, MediaPermissions.SermonsPublish, "media.sermon.published", (sermon, now) =>
        {
            sermon.Publish(now);
            return Task.FromResult(Result.Success());
        }, cancellationToken);

    public Task<Result<SermonAdminDto>> ScheduleAsync(Guid id, ScheduleRequest request, CancellationToken cancellationToken) =>
        ChangeAsync(id, MediaPermissions.SermonsPublish, "media.sermon.scheduled", (sermon, now) =>
        {
            sermon.Schedule(request.PublishAt, now);
            return Task.FromResult(Result.Success());
        }, cancellationToken);

    public Task<Result<SermonAdminDto>> UnpublishAsync(Guid id, CancellationToken cancellationToken) =>
        ChangeAsync(id, MediaPermissions.SermonsPublish, "media.sermon.unpublished", (sermon, now) =>
        {
            sermon.Unpublish(now);
            return Task.FromResult(Result.Success());
        }, cancellationToken);

    public Task<Result<SermonAdminDto>> ArchiveAsync(Guid id, CancellationToken cancellationToken) =>
        ChangeAsync(id, MediaPermissions.SermonsPublish, "media.sermon.archived", (sermon, now) =>
        {
            sermon.Archive(now);
            return Task.FromResult(Result.Success());
        }, cancellationToken);

    public Task<Result<SermonAdminDto>> RestoreAsync(Guid id, CancellationToken cancellationToken) =>
        ChangeAsync(id, MediaPermissions.SermonsPublish, "media.sermon.restored", (sermon, now) =>
        {
            sermon.Restore(now);
            return Task.FromResult(Result.Success());
        }, cancellationToken);

    /// <summary>Only drafts that were never public can be deleted; anything once published is archived instead.</summary>
    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var sermon = await db.Sermons.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (sermon is null || !await CanAsync(MediaPermissions.SermonsEdit, sermon, cancellationToken))
        {
            return NotFound;
        }

        if (sermon.Status != SermonStatus.Draft || sermon.PublishedAt is not null)
        {
            return Error.Conflict("media.cannot_delete", "Only unpublished drafts can be deleted. Archive this sermon instead.");
        }

        db.Sermons.Remove(sermon);
        await db.SaveChangesAsync(cancellationToken);
        await AuditAsync("media.sermon.deleted", sermon, cancellationToken);
        return Result.Success();
    }

    public static ScriptureCheckDto CheckScripture(string? text)
    {
        var recognised = ScriptureReference.ParseMany(text, out var unrecognised);
        return new ScriptureCheckDto(recognised.Select(r => r.ToString()).ToList(), unrecognised);
    }

    private async Task<Result<SermonAdminDto>> ChangeAsync(
        Guid id,
        string permission,
        string auditAction,
        Func<Sermon, DateTimeOffset, Task<Result>> change,
        CancellationToken cancellationToken)
    {
        var sermon = await db.Sermons.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (sermon is null || !await CanAsync(MediaPermissions.SermonsEdit, sermon, cancellationToken))
        {
            return NotFound;
        }

        if (permission != MediaPermissions.SermonsEdit && !await CanAsync(permission, sermon, cancellationToken))
        {
            return Error.Forbidden("media.forbidden", "You can edit this sermon but not publish it. Ask someone who publishes sermons.");
        }

        var result = await change(sermon, clock.GetUtcNow());
        if (result.IsFailure)
        {
            return result.Error!;
        }

        await RefreshSearchTextAsync(sermon, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await AuditAsync(auditAction, sermon, cancellationToken);
        return await ToAdminDtoAsync(sermon, cancellationToken);
    }

    private async Task<Result> ApplyAsync(Sermon sermon, SaveSermonRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (request.SeriesId is { } seriesId && !await db.Series.AnyAsync(s => s.Id == seriesId, cancellationToken))
        {
            return Error.NotFound("media.series_not_found", "Series not found.");
        }

        var speakerIds = request.SpeakerIds.Distinct().ToList();
        var known = await db.Speakers.CountAsync(s => speakerIds.Contains(s.Id), cancellationToken);
        if (known != speakerIds.Count)
        {
            return Error.NotFound("media.speaker_not_found", "One of the speakers wasn't found.");
        }

        var scripture = ScriptureReference.ParseMany(request.Scripture, out var unrecognised);
        if (unrecognised.Count > 0)
        {
            return new Error("media.scripture_invalid", $"We couldn't read: {string.Join("; ", unrecognised)}. Try a form like \"Psalm 42:1-11\", separated by semicolons.");
        }

        VideoLink? video = null;
        if (!string.IsNullOrWhiteSpace(request.VideoUrl) && !VideoLink.TryParseYouTube(request.VideoUrl, out video))
        {
            return new Error("media.video_invalid", "Paste a YouTube link, e.g. https://www.youtube.com/watch?v=…");
        }

        sermon.UpdateDetails(request.Title, request.PreachedOn, request.SeriesId, request.Summary, request.Notes, request.Topics, now);
        sermon.SetSpeakers(speakerIds, now);
        sermon.SetScripture(scripture, now);
        sermon.SetVideo(video, now);
        await RefreshSearchTextAsync(sermon, cancellationToken);
        return Result.Success();
    }

    private async Task RefreshSearchTextAsync(Sermon sermon, CancellationToken cancellationToken)
    {
        var ids = sermon.Speakers.Select(s => s.SpeakerId).ToList();
        var names = await db.Speakers.AsNoTracking().Where(s => ids.Contains(s.Id)).Select(s => s.Name).ToListAsync(cancellationToken);
        var seriesTitle = sermon.SeriesId is { } seriesId
            ? await db.Series.AsNoTracking().Where(s => s.Id == seriesId).Select(s => s.Title).SingleOrDefaultAsync(cancellationToken)
            : null;
        sermon.RefreshSearchText(names, seriesTitle);
    }

    private async Task<string> UniqueSlugAsync(string slug, CancellationToken cancellationToken)
    {
        var candidate = slug;
        for (var n = 2; await db.Sermons.AnyAsync(s => s.Slug == candidate, cancellationToken); n++)
        {
            candidate = $"{slug}-{n}";
        }

        return candidate;
    }

    private async Task<SermonAdminDto> ToAdminDtoAsync(Sermon sermon, CancellationToken cancellationToken) =>
        new(await reader.DetailAsync(sermon, cancellationToken), sermon.Status, sermon.PublishAt, sermon.Scope, sermon.ImportSource, sermon.PublishProblems(), sermon.UpdatedAt);

    private Task<bool> CanAsync(string permission, Sermon sermon, CancellationToken cancellationToken) =>
        authorizer.CanAsync(permission, ScopePath.Parse(sermon.Scope), cancellationToken);

    private Task AuditAsync(string action, Sermon sermon, CancellationToken cancellationToken) =>
        audit.RecordAsync(new AuditRecord(action, "sermon", sermon.Id.ToString(), ScopePath.Parse(sermon.Scope), new { sermon.Title, Status = sermon.Status.ToString() }), cancellationToken);
}
