using Microsoft.EntityFrameworkCore;
using Shapers.Church.Contracts;
using Shapers.Media.Contracts;
using Shapers.Media.Domain;
using Shapers.Platform.Authorization;

namespace Shapers.Media.Application;

public sealed class SeriesService(IMediaDb db, SermonReader reader, IAuthorizer authorizer, IChurchDirectory church, TimeProvider clock)
{
    public async Task<IReadOnlyList<SeriesDto>> ListAsync(CancellationToken cancellationToken)
    {
        var series = await db.Series.AsNoTracking().OrderByDescending(s => s.StartsOn ?? DateOnly.MinValue).ThenBy(s => s.Title).ToListAsync(cancellationToken);
        var result = new List<SeriesDto>();
        foreach (var s in series)
        {
            result.Add(await reader.SeriesDtoAsync(s, cancellationToken, publishedOnly: false));
        }

        return result;
    }

    public async Task<Result<SeriesDto>> CreateAsync(SaveSeriesRequest request, CancellationToken cancellationToken)
    {
        var root = ScopePath.Parse((await church.GetRootScopeAsync(cancellationToken)).Path);
        if (!await authorizer.CanAsync(MediaPermissions.SermonsEdit, root, cancellationToken))
        {
            return Error.Forbidden("media.forbidden", "Only church-wide editors can add series.");
        }

        var series = Series.Create(request.Title, root, clock.GetUtcNow());
        series.Update(request.Title, request.Description, request.StartsOn, request.EndsOn);
        var artwork = await CheckImageAsync(request.ArtworkAssetId, cancellationToken);
        if (artwork.IsFailure)
        {
            return artwork.Error!;
        }

        series.SetArtwork(request.ArtworkAssetId);
        var slug = series.Slug;
        for (var n = 2; await db.Series.AnyAsync(s => s.Slug == slug, cancellationToken); n++)
        {
            slug = $"{series.Slug}-{n}";
        }

        series.UseSlug(slug);
        db.Series.Add(series);
        await db.SaveChangesAsync(cancellationToken);
        return await reader.SeriesDtoAsync(series, cancellationToken, publishedOnly: false);
    }

    public async Task<Result<SeriesDto>> UpdateAsync(Guid id, SaveSeriesRequest request, CancellationToken cancellationToken)
    {
        var series = await db.Series.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (series is null || !await authorizer.CanAsync(MediaPermissions.SermonsEdit, ScopePath.Parse(series.Scope), cancellationToken))
        {
            return Error.NotFound("media.series_not_found", "Series not found.");
        }

        var artwork = await CheckImageAsync(request.ArtworkAssetId, cancellationToken);
        if (artwork.IsFailure)
        {
            return artwork.Error!;
        }

        series.Update(request.Title, request.Description, request.StartsOn, request.EndsOn);
        series.SetArtwork(request.ArtworkAssetId);
        await db.SaveChangesAsync(cancellationToken);
        return await reader.SeriesDtoAsync(series, cancellationToken, publishedOnly: false);
    }

    private async Task<Result> CheckImageAsync(Guid? assetId, CancellationToken cancellationToken) =>
        assetId is null || await db.Assets.AnyAsync(a => a.Id == assetId && a.Kind == MediaKind.Image && a.Status == MediaAssetStatus.Ready, cancellationToken)
            ? Result.Success()
            : new Error("media.image_invalid", "Upload an image (JPEG, PNG or WebP) first.");
}

public sealed class SpeakerService(IMediaDb db, SermonReader reader, IAuthorizer authorizer)
{
    public async Task<IReadOnlyList<SpeakerDto>> ListAsync(bool includeInactive, CancellationToken cancellationToken)
    {
        var speakers = await db.Speakers.AsNoTracking()
            .Where(s => includeInactive || s.IsActive)
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);
        var photoIds = speakers.Where(s => s.PhotoAssetId is not null).Select(s => s.PhotoAssetId!.Value).ToList();
        var assets = await db.Assets.AsNoTracking().Where(a => photoIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, cancellationToken);
        return speakers.Select(s => reader.SpeakerDto(s, assets)).ToList();
    }

    public async Task<Result<SpeakerDto>> SaveAsync(Guid? id, SaveSpeakerRequest request, CancellationToken cancellationToken)
    {
        if (!await authorizer.HasAnywhereAsync(MediaPermissions.SpeakersManage, cancellationToken))
        {
            return Error.Forbidden("media.forbidden", "You can't manage speakers.");
        }

        if (request.PhotoAssetId is { } photoId
            && !await db.Assets.AnyAsync(a => a.Id == photoId && a.Kind == MediaKind.Image && a.Status == MediaAssetStatus.Ready, cancellationToken))
        {
            return new Error("media.image_invalid", "Upload a photo (JPEG, PNG or WebP) first.");
        }

        Speaker? speaker;
        if (id is { } existingId)
        {
            speaker = await db.Speakers.SingleOrDefaultAsync(s => s.Id == existingId, cancellationToken);
            if (speaker is null)
            {
                return Error.NotFound("media.speaker_not_found", "Speaker not found.");
            }

            speaker.Update(request.Name, request.Title, request.Bio, request.PersonId);
        }
        else
        {
            speaker = Speaker.Create(request.Name, request.Title, request.Bio, request.PersonId);
            db.Speakers.Add(speaker);
        }

        speaker.SetPhoto(request.PhotoAssetId);
        await db.SaveChangesAsync(cancellationToken);
        var assets = await db.Assets.AsNoTracking().Where(a => a.Id == speaker.PhotoAssetId).ToDictionaryAsync(a => a.Id, cancellationToken);
        return reader.SpeakerDto(speaker, assets);
    }
}

/// <summary>Two-step upload: reserve a storage slot and get a URL; the client uploads directly; then confirm.</summary>
public sealed class UploadService(IMediaDb db, IFileStorage storage, IAuthorizer authorizer, ICurrentUser currentUser, TimeProvider clock)
{
    private static readonly TimeSpan UploadWindow = TimeSpan.FromMinutes(30);

    public async Task<Result<StartUploadResponse>> StartAsync(StartUploadRequest request, CancellationToken cancellationToken)
    {
        if (!await authorizer.HasAnywhereAsync(MediaPermissions.SermonsEdit, cancellationToken)
            && !await authorizer.HasAnywhereAsync(MediaPermissions.SpeakersManage, cancellationToken))
        {
            return Error.Forbidden("media.forbidden", "You can't upload media.");
        }

        var asset = MediaAsset.StartUpload(request.Kind, request.FileName, request.ContentType, request.SizeBytes, currentUser.UserId, clock.GetUtcNow());
        db.Assets.Add(asset);
        await db.SaveChangesAsync(cancellationToken);
        return new StartUploadResponse(asset.Id, storage.CreateUpload(asset.StorageKey, asset.ContentType, asset.SizeBytes, UploadWindow));
    }

    public async Task<Result<AssetDto>> CompleteAsync(Guid id, CompleteUploadRequest request, CancellationToken cancellationToken)
    {
        var asset = await db.Assets.SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (asset is null || (asset.UploadedByUserId is not null && asset.UploadedByUserId != currentUser.UserId))
        {
            return Error.NotFound("media.asset_not_found", "Upload not found.");
        }

        if (asset.Status == MediaAssetStatus.Pending)
        {
            var size = await storage.GetSizeAsync(asset.StorageKey, cancellationToken);
            asset.MarkReady(size ?? 0, request.DurationSeconds);
            await db.SaveChangesAsync(cancellationToken);
        }

        return new AssetDto(asset.Id, asset.Kind, storage.PublicUrl(asset.StorageKey), asset.ContentType, asset.SizeBytes, asset.DurationSeconds, asset.Status);
    }
}
