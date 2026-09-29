using System.Globalization;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NpgsqlTypes;
using Shapers.Media.Domain;
using Shapers.Platform.Authorization;
using Shapers.Platform.Web;

namespace Shapers.Media.Application;

/// <summary>What anyone can see: published sermons, their series and speakers. No sign-in needed.</summary>
public sealed class PublicMediaService(IMediaDb db, SermonReader reader)
{
    public async Task<PagedResult<SermonSummaryDto>> SearchAsync(SermonSearchQuery request, CancellationToken cancellationToken)
    {
        var paging = new PageRequest(request.Page, Math.Min(request.PageSize, 50));
        var query = db.Sermons.AsNoTracking().Where(s => s.Status == SermonStatus.Published);

        if (!string.IsNullOrWhiteSpace(request.Series))
        {
            query = query.Where(s => db.Series.Any(x => x.Id == s.SeriesId && x.Slug == request.Series));
        }

        if (request.SpeakerId is { } speakerId)
        {
            query = query.Where(s => s.Speakers.Any(x => x.SpeakerId == speakerId));
        }

        if (request.Book is { } book)
        {
            query = query.Where(s => s.Scripture.Any(r => r.BookNumber == book));
        }

        if (!string.IsNullOrWhiteSpace(request.Topic))
        {
            query = query.Where(s => s.Topics.Contains(request.Topic));
        }

        if (request.Year is { } year)
        {
            query = query.Where(s => s.PreachedOn >= new DateOnly(year, 1, 1) && s.PreachedOn <= new DateOnly(year, 12, 31));
        }

        IOrderedQueryable<Sermon> ordered;
        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var q = request.Q.Trim();
            query = query.Where(s => EF.Property<NpgsqlTsVector>(s, "SearchVector").Matches(EF.Functions.WebSearchToTsQuery("english", q)));
            ordered = query.OrderByDescending(s => EF.Property<NpgsqlTsVector>(s, "SearchVector").Rank(EF.Functions.WebSearchToTsQuery("english", q)))
                .ThenByDescending(s => s.PreachedOn);
        }
        else
        {
            ordered = query.OrderByDescending(s => s.PreachedOn).ThenByDescending(s => s.PublishedAt);
        }

        var total = await query.CountAsync(cancellationToken);
        var sermons = await ordered.Skip(paging.Skip).Take(paging.SafePageSize).ToListAsync(cancellationToken);
        return new PagedResult<SermonSummaryDto>(await reader.SummariesAsync(sermons, cancellationToken), paging.SafePage, paging.SafePageSize, total);
    }

    public async Task<Result<SermonDetailDto>> GetAsync(string slugOrId, CancellationToken cancellationToken)
    {
        var sermon = Guid.TryParse(slugOrId, out var id)
            ? await db.Sermons.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id && s.Status == SermonStatus.Published, cancellationToken)
            : await db.Sermons.AsNoTracking().SingleOrDefaultAsync(s => s.Slug == slugOrId && s.Status == SermonStatus.Published, cancellationToken);
        return sermon is null
            ? Error.NotFound("media.sermon_not_found", "Sermon not found.")
            : await reader.DetailAsync(sermon, cancellationToken);
    }

    /// <summary>Series that have at least one published sermon, newest first.</summary>
    public async Task<IReadOnlyList<SeriesDto>> SeriesAsync(CancellationToken cancellationToken)
    {
        var series = await db.Series.AsNoTracking()
            .Where(x => db.Sermons.Any(s => s.SeriesId == x.Id && s.Status == SermonStatus.Published))
            .ToListAsync(cancellationToken);
        var latest = await db.Sermons.AsNoTracking()
            .Where(s => s.Status == SermonStatus.Published && s.SeriesId != null)
            .GroupBy(s => s.SeriesId!.Value)
            .Select(g => new { g.Key, Latest = g.Max(s => s.PreachedOn) })
            .ToDictionaryAsync(x => x.Key, x => x.Latest, cancellationToken);

        var result = new List<SeriesDto>();
        foreach (var s in series.OrderByDescending(x => latest.GetValueOrDefault(x.Id)))
        {
            result.Add(await reader.SeriesDtoAsync(s, cancellationToken));
        }

        return result;
    }

    public async Task<Result<SeriesDto>> SeriesBySlugAsync(string slug, CancellationToken cancellationToken)
    {
        var series = await db.Series.AsNoTracking().SingleOrDefaultAsync(s => s.Slug == slug, cancellationToken);
        return series is null
            ? Error.NotFound("media.series_not_found", "Series not found.")
            : await reader.SeriesDtoAsync(series, cancellationToken);
    }

    public async Task<IReadOnlyList<SpeakerDto>> SpeakersAsync(CancellationToken cancellationToken)
    {
        var speakerIds = await db.Sermons.AsNoTracking()
            .Where(s => s.Status == SermonStatus.Published)
            .SelectMany(s => s.Speakers.Select(x => x.SpeakerId))
            .Distinct()
            .ToListAsync(cancellationToken);
        var speakers = await db.Speakers.AsNoTracking().Where(s => speakerIds.Contains(s.Id)).OrderBy(s => s.Name).ToListAsync(cancellationToken);
        var photoIds = speakers.Where(s => s.PhotoAssetId is not null).Select(s => s.PhotoAssetId!.Value).ToList();
        var assets = await db.Assets.AsNoTracking().Where(a => photoIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, cancellationToken);
        return speakers.Select(s => reader.SpeakerDto(s, assets)).ToList();
    }
}

/// <summary>A signed-in member's listening progress (PERSONAL scope).</summary>
public sealed class PlaybackService(IMediaDb db, SermonReader reader, ICurrentUser currentUser, TimeProvider clock)
{
    public async Task<Result> UpdateAsync(Guid sermonId, PlaybackUpdateRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.PersonId is not { } personId)
        {
            return Error.Unauthorized("media.no_profile", "Sign in to keep your place.");
        }

        var duration = await db.Sermons.AsNoTracking()
            .Where(s => s.Id == sermonId && s.Status == SermonStatus.Published)
            .Select(s => new { s.AudioDurationSeconds })
            .SingleOrDefaultAsync(cancellationToken);
        if (duration is null)
        {
            return Error.NotFound("media.sermon_not_found", "Sermon not found.");
        }

        var position = await db.PlaybackPositions.SingleOrDefaultAsync(p => p.PersonId == personId && p.SermonId == sermonId, cancellationToken);
        if (position is null)
        {
            position = PlaybackPosition.Start(personId, sermonId, clock.GetUtcNow());
            db.PlaybackPositions.Add(position);
        }

        position.Update(request.PositionSeconds, duration.AudioDurationSeconds, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<IReadOnlyList<ContinueListeningDto>> ContinueAsync(CancellationToken cancellationToken)
    {
        if (currentUser.PersonId is not { } personId)
        {
            return [];
        }

        var positions = await db.PlaybackPositions.AsNoTracking()
            .Where(p => p.PersonId == personId && !p.Completed && p.PositionSeconds > 30)
            .OrderByDescending(p => p.UpdatedAt)
            .Take(10)
            .ToListAsync(cancellationToken);
        var ids = positions.Select(p => p.SermonId).ToList();
        var sermons = await db.Sermons.AsNoTracking().Where(s => ids.Contains(s.Id) && s.Status == SermonStatus.Published).ToListAsync(cancellationToken);
        var summaries = (await reader.SummariesAsync(sermons, cancellationToken)).ToDictionary(s => s.Id);
        return positions
            .Where(p => summaries.ContainsKey(p.SermonId))
            .Select(p => new ContinueListeningDto(summaries[p.SermonId], p.PositionSeconds, p.UpdatedAt))
            .ToList();
    }
}

/// <summary>Publishes scheduled sermons when their time comes. Runs every minute.</summary>
public sealed class ScheduledPublisher(IMediaDb db, TimeProvider clock)
{
    public async Task<int> PublishDueAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var due = await db.Sermons.Where(s => s.Status == SermonStatus.Scheduled && s.PublishAt <= now).ToListAsync(cancellationToken);
        var published = due.Count(s => s.PublishIfDue(now));
        if (published > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return published;
    }

    /// <summary>Data minimisation: listening positions older than six months are removed.</summary>
    public Task<int> PurgeOldPlaybackAsync(CancellationToken cancellationToken)
    {
        var cutoff = clock.GetUtcNow() - PlaybackPosition.Retention;
        return db.PlaybackPositions.Where(p => p.UpdatedAt < cutoff).ExecuteDeleteAsync(cancellationToken);
    }
}

/// <summary>An RSS feed of published sermons with audio, in the format Apple Podcasts and Spotify expect.</summary>
public sealed class PodcastFeedBuilder(IMediaDb db, SermonReader reader, IFileStorage storage, IOptions<MediaOptions> options)
{
    private static readonly XNamespace Itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";

    public async Task<string> BuildAsync(string feedUrl, CancellationToken cancellationToken)
    {
        var o = options.Value;
        var sermons = await db.Sermons.AsNoTracking()
            .Where(s => s.Status == SermonStatus.Published && s.AudioAssetId != null)
            .OrderByDescending(s => s.PreachedOn).ThenByDescending(s => s.PublishedAt)
            .Take(300)
            .ToListAsync(cancellationToken);
        var (speakers, series, assets) = await reader.LoadRelatedAsync(sermons, cancellationToken);

        var channel = new XElement("channel",
            new XElement("title", o.Podcast.Title),
            new XElement("link", o.SiteUrl),
            new XElement("language", o.Podcast.Language),
            new XElement("description", o.Podcast.Description),
            new XElement(Atom + "link", new XAttribute("href", feedUrl), new XAttribute("rel", "self"), new XAttribute("type", "application/rss+xml")),
            new XElement(Itunes + "author", o.Podcast.Author),
            new XElement(Itunes + "owner", new XElement(Itunes + "name", o.Podcast.Author), new XElement(Itunes + "email", o.Podcast.Email)),
            new XElement(Itunes + "explicit", "false"),
            new XElement(Itunes + "category", new XAttribute("text", o.Podcast.Category), new XElement(Itunes + "category", new XAttribute("text", o.Podcast.SubCategory))));
        if (o.Podcast.ImageUrl is { } image)
        {
            channel.Add(new XElement(Itunes + "image", new XAttribute("href", image)));
        }

        foreach (var sermon in sermons)
        {
            if (!assets.TryGetValue(sermon.AudioAssetId!.Value, out var audio))
            {
                continue;
            }

            var names = sermon.Speakers.OrderBy(x => x.Order).Select(x => speakers.GetValueOrDefault(x.SpeakerId)?.Name).OfType<string>().ToList();
            var seriesTitle = sermon.SeriesId is { } sid ? series.GetValueOrDefault(sid)?.Title : null;
            var scripture = string.Join("; ", sermon.Scripture.Select(r => r.ToString()));
            var description = string.Join(" · ", new[] { sermon.Summary, scripture, seriesTitle is null ? null : $"Series: {seriesTitle}" }.Where(x => !string.IsNullOrWhiteSpace(x)));
            var pubDate = sermon.PublishedAt ?? new DateTimeOffset(sermon.PreachedOn.ToDateTime(new TimeOnly(9, 0)), TimeSpan.FromHours(2));

            var item = new XElement("item",
                new XElement("title", sermon.Title),
                new XElement("link", $"{o.SiteUrl.TrimEnd('/')}/sermons/{sermon.Slug}"),
                new XElement("guid", new XAttribute("isPermaLink", "false"), sermon.Id.ToString()),
                new XElement("pubDate", pubDate.ToString("r", CultureInfo.InvariantCulture)),
                new XElement("description", description.Length > 0 ? description : sermon.Title),
                new XElement("enclosure",
                    new XAttribute("url", storage.PublicUrl(audio.StorageKey)),
                    new XAttribute("length", audio.SizeBytes.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("type", audio.ContentType)),
                new XElement(Itunes + "author", names.Count > 0 ? string.Join(", ", names) : o.Podcast.Author),
                new XElement(Itunes + "explicit", "false"));
            if (audio.DurationSeconds is { } seconds)
            {
                item.Add(new XElement(Itunes + "duration", seconds.ToString(CultureInfo.InvariantCulture)));
            }

            channel.Add(item);
        }

        var rss = new XElement("rss",
            new XAttribute("version", "2.0"),
            new XAttribute(XNamespace.Xmlns + "itunes", Itunes),
            new XAttribute(XNamespace.Xmlns + "atom", Atom),
            channel);
        var document = new XDocument(new XDeclaration("1.0", "utf-8", null), rss);
        return $"{document.Declaration}\n{document.ToString(SaveOptions.None)}";
    }
}
