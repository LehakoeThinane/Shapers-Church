using Microsoft.EntityFrameworkCore;
using Shapers.Media.Domain;

namespace Shapers.Media.Application;

/// <summary>Builds sermon read models, loading speakers, series and files in bulk rather than per sermon.</summary>
public sealed class SermonReader(IMediaDb db, IFileStorage storage)
{
    public async Task<IReadOnlyList<SermonSummaryDto>> SummariesAsync(IReadOnlyList<Sermon> sermons, CancellationToken cancellationToken)
    {
        var (speakers, series, _) = await LoadRelatedAsync(sermons, cancellationToken);
        return sermons.Select(s => Summary(s, speakers, series)).ToList();
    }

    public async Task<SermonDetailDto> DetailAsync(Sermon sermon, CancellationToken cancellationToken)
    {
        var (speakers, series, assets) = await LoadRelatedAsync([sermon], cancellationToken);
        var seriesDto = sermon.SeriesId is { } seriesId && series.TryGetValue(seriesId, out var s)
            ? await SeriesDtoAsync(s, cancellationToken)
            : null;

        AudioDto? audio = null;
        if (sermon.AudioAssetId is { } audioId && assets.TryGetValue(audioId, out var audioAsset))
        {
            audio = new AudioDto(audioAsset.Id, storage.PublicUrl(audioAsset.StorageKey), audioAsset.ContentType, audioAsset.SizeBytes, audioAsset.DurationSeconds);
        }

        var pdf = sermon.NotesPdfAssetId is { } pdfId && assets.TryGetValue(pdfId, out var pdfAsset) ? storage.PublicUrl(pdfAsset.StorageKey) : null;

        return new SermonDetailDto(
            sermon.Id,
            sermon.Title,
            sermon.Slug,
            sermon.PreachedOn,
            sermon.Summary,
            sermon.Notes,
            sermon.Speakers.OrderBy(x => x.Order).Where(x => speakers.ContainsKey(x.SpeakerId)).Select(x => SpeakerDto(speakers[x.SpeakerId], assets)).ToList(),
            seriesDto,
            sermon.Scripture.Select(ScriptureDto).ToList(),
            sermon.Topics,
            sermon.Video is { } v ? new VideoDto(v.Provider, v.ExternalId, v.WatchUrl, v.ThumbnailUrl) : null,
            audio,
            pdf,
            sermon.PublishedAt);
    }

    public async Task<SeriesDto> SeriesDtoAsync(Series series, CancellationToken cancellationToken, bool publishedOnly = true)
    {
        var count = await db.Sermons.CountAsync(
            s => s.SeriesId == series.Id && (!publishedOnly || s.Status == SermonStatus.Published), cancellationToken);
        var artwork = series.ArtworkAssetId is { } artId
            ? await db.Assets.AsNoTracking().Where(a => a.Id == artId).Select(a => a.StorageKey).SingleOrDefaultAsync(cancellationToken)
            : null;
        return new SeriesDto(series.Id, series.Title, series.Slug, series.Description, artwork is null ? null : storage.PublicUrl(artwork), series.StartsOn, series.EndsOn, series.Scope, count);
    }

    public SpeakerDto SpeakerDto(Speaker speaker, IReadOnlyDictionary<Guid, MediaAsset> assets) =>
        new(
            speaker.Id,
            speaker.Name,
            speaker.Title,
            speaker.Bio,
            speaker.PhotoAssetId is { } photo && assets.TryGetValue(photo, out var asset) ? storage.PublicUrl(asset.StorageKey) : null,
            speaker.PersonId,
            speaker.IsActive);

    public static ScriptureDto ScriptureDto(ScriptureReference r) =>
        new(r.ToString(), r.BookNumber, r.Book.Name, r.ChapterFrom, r.VerseFrom, r.ChapterTo, r.VerseTo);

    public async Task<(Dictionary<Guid, Speaker> Speakers, Dictionary<Guid, Series> Series, Dictionary<Guid, MediaAsset> Assets)> LoadRelatedAsync(
        IReadOnlyList<Sermon> sermons,
        CancellationToken cancellationToken)
    {
        var speakerIds = sermons.SelectMany(s => s.Speakers.Select(x => x.SpeakerId)).Distinct().ToList();
        var seriesIds = sermons.Where(s => s.SeriesId is not null).Select(s => s.SeriesId!.Value).Distinct().ToList();

        var speakers = await db.Speakers.AsNoTracking().Where(s => speakerIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, cancellationToken);
        var series = await db.Series.AsNoTracking().Where(s => seriesIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, cancellationToken);

        var assetIds = sermons.SelectMany(s => new[] { s.AudioAssetId, s.NotesPdfAssetId })
            .Concat(speakers.Values.Select(s => s.PhotoAssetId))
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        var assets = await db.Assets.AsNoTracking().Where(a => assetIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, cancellationToken);
        return (speakers, series, assets);
    }

    private static SermonSummaryDto Summary(Sermon s, IReadOnlyDictionary<Guid, Speaker> speakers, IReadOnlyDictionary<Guid, Series> series)
    {
        var seriesEntity = s.SeriesId is { } id ? series.GetValueOrDefault(id) : null;
        return new SermonSummaryDto(
            s.Id,
            s.Title,
            s.Slug,
            s.PreachedOn,
            s.Speakers.OrderBy(x => x.Order).Select(x => speakers.GetValueOrDefault(x.SpeakerId)?.Name).OfType<string>().ToList(),
            seriesEntity?.Title,
            seriesEntity?.Slug,
            s.Scripture.Select(r => r.ToString()).ToList(),
            s.Video?.ThumbnailUrl,
            s.AudioDurationSeconds,
            s.AudioAssetId is not null,
            s.Video is not null);
    }
}
