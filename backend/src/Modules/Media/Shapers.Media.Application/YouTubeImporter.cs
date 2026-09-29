using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shapers.Church.Contracts;
using Shapers.Media.Contracts;
using Shapers.Media.Domain;
using Shapers.Platform.Auditing;
using Shapers.Platform.Authorization;

namespace Shapers.Media.Application;

/// <summary>
/// Brings the church's existing YouTube sermons in as drafts, so staff only add speakers and series before
/// publishing. Safe to run repeatedly: videos already imported are skipped. Reads metadata only; never
/// downloads video or audio from YouTube.
/// </summary>
public sealed class YouTubeImporter(
    IMediaDb db,
    IYouTubeClient youTube,
    IAuthorizer authorizer,
    IChurchDirectory church,
    IAuditLog audit,
    IOptions<MediaOptions> options,
    TimeProvider clock)
{
    private static readonly TimeZoneInfo Johannesburg = TimeZoneInfo.FindSystemTimeZoneById("Africa/Johannesburg");

    public async Task<Result<ImportResultDto>> ImportAsync(CancellationToken cancellationToken)
    {
        var root = ScopePath.Parse((await church.GetRootScopeAsync(cancellationToken)).Path);
        if (!await authorizer.CanAsync(MediaPermissions.SermonsEdit, root, cancellationToken))
        {
            return Error.Forbidden("media.forbidden", "Only church-wide editors can import sermons.");
        }

        if (!youTube.IsConfigured)
        {
            return new Error("media.youtube_not_configured", "Add a YouTube Data API key (Media:YouTube:ApiKey) to import from YouTube.");
        }

        var videos = await youTube.ListChannelVideosAsync(options.Value.YouTube.ChannelId, cancellationToken);
        var sources = videos.Select(v => Source(v.VideoId)).ToList();
        var existing = (await db.Sermons.AsNoTracking()
            .Where(s => s.ImportSource != null && sources.Contains(s.ImportSource))
            .Select(s => s.ImportSource!)
            .ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
        var usedSlugs = (await db.Sermons.AsNoTracking().Select(s => s.Slug).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);

        var now = clock.GetUtcNow();
        var created = new List<string>();
        foreach (var video in videos.Where(v => !existing.Contains(Source(v.VideoId))))
        {
            var title = Decode(video.Title);
            var scripture = new List<ScriptureReference>();
            if (ScriptureReference.TryExtractFromTitle(title, out var reference, out var remainder))
            {
                scripture.Add(reference);
                title = remainder;
            }

            var preachedOn = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(video.PublishedAt, Johannesburg).DateTime);
            var sermon = Sermon.Create(title.Length > 200 ? title[..200] : title, preachedOn, root, now, Source(video.VideoId));
            var slug = sermon.Slug;
            for (var n = 2; usedSlugs.Contains(slug); n++)
            {
                slug = $"{sermon.Slug}-{n.ToString(CultureInfo.InvariantCulture)}";
            }

            sermon.UseSlug(slug);
            usedSlugs.Add(slug);
            sermon.SetScripture(scripture, now);
            sermon.SetVideo(new VideoLink(VideoProvider.YouTube, video.VideoId), now);
            sermon.RefreshSearchText([], null);
            db.Sermons.Add(sermon);
            created.Add(sermon.Title);
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord("media.youtube.imported", "sermon", null, root, new { found = videos.Count, created = created.Count }), cancellationToken);
        return new ImportResultDto(videos.Count, created.Count, videos.Count - created.Count, created);
    }

    private static string Source(string videoId) => $"youtube:{videoId}";

    private static string Decode(string title) => System.Net.WebUtility.HtmlDecode(title).Trim();
}
