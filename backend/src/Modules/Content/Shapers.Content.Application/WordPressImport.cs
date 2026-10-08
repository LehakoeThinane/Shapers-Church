using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shapers.Church.Contracts;
using Shapers.Content.Domain;
using Shapers.Media.Contracts;
using Shapers.Platform.Auditing;

namespace Shapers.Content.Application;

public sealed class WordPressOptions
{
    public const string SectionName = "Content:WordPress";

    /// <summary>The old website to import from.</summary>
    public string SiteUrl { get; set; } = "https://shaperschurch.com";
}

/// <summary>A file downloaded from the old site.</summary>
public sealed record DownloadedFile(byte[] Content, string ContentType);

/// <summary>One post or page as WordPress's public REST API returns it.</summary>
public sealed record WordPressItem(string Slug, string Link, DateTimeOffset Date, string TitleHtml, string ExcerptHtml, string ContentHtml, string? FeaturedImageUrl);

/// <summary>Reads the old site's public content. Replaced in tests.</summary>
public interface IWordPressSource
{
    Task<IReadOnlyList<WordPressItem>> PostsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<WordPressItem>> PagesAsync(CancellationToken cancellationToken);

    /// <summary>The address of an uploaded image, which the page builder refers to only by number.</summary>
    Task<string?> MediaUrlAsync(int id, CancellationToken cancellationToken);

    /// <summary>Downloads one of the old site's uploaded files. Null when it can't be fetched or is too large.</summary>
    Task<DownloadedFile?> DownloadAsync(string url, CancellationToken cancellationToken);
}

public sealed class WordPressHttpSource(HttpClient http) : IWordPressSource
{
    /// <summary>Generous for a photo, small enough that a mistake can't fill the storage.</summary>
    public const long MaxDownloadBytes = 15 * 1024 * 1024;

    public async Task<DownloadedFile?> DownloadAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(new Uri(url), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxDownloadBytes)
        {
            return null;
        }

        var content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return content.Length > MaxDownloadBytes
            ? null
            : new DownloadedFile(content, response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream");
    }

    public Task<IReadOnlyList<WordPressItem>> PostsAsync(CancellationToken cancellationToken) => ReadAsync("posts", cancellationToken);

    public Task<IReadOnlyList<WordPressItem>> PagesAsync(CancellationToken cancellationToken) => ReadAsync("pages", cancellationToken);

    public async Task<string?> MediaUrlAsync(int id, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync($"wp-json/wp/v2/media/{id}?_fields=source_url", cancellationToken);
        return response.IsSuccessStatusCode ? (await response.Content.ReadFromJsonAsync<Media>(cancellationToken))?.SourceUrl : null;
    }

    private async Task<IReadOnlyList<WordPressItem>> ReadAsync(string type, CancellationToken cancellationToken)
    {
        var items = new List<WordPressItem>();
        for (var page = 1; page <= 20; page++)
        {
            using var response = await http.GetAsync($"wp-json/wp/v2/{type}?per_page=100&page={page}&_embed=wp:featuredmedia", cancellationToken);
            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                break; // Past the last page.
            }

            response.EnsureSuccessStatusCode();
            var batch = await response.Content.ReadFromJsonAsync<List<WpItem>>(cancellationToken) ?? [];
            items.AddRange(batch.Select(i => new WordPressItem(
                i.Slug,
                i.Link,
                new DateTimeOffset(DateTime.SpecifyKind(i.DateGmt ?? i.Date, DateTimeKind.Utc)),
                i.Title?.Rendered ?? string.Empty,
                i.Excerpt?.Rendered ?? string.Empty,
                i.Content?.Rendered ?? string.Empty,
                i.Embedded?.FeaturedMedia?.FirstOrDefault()?.SourceUrl)));
            if (batch.Count < 100)
            {
                break;
            }
        }

        return items;
    }

    private sealed record RenderedText([property: JsonPropertyName("rendered")] string Rendered);

    private sealed record Media([property: JsonPropertyName("source_url")] string? SourceUrl);

    private sealed record Embedded([property: JsonPropertyName("wp:featuredmedia")] List<Media>? FeaturedMedia);

    private sealed record WpItem(
        [property: JsonPropertyName("slug")] string Slug,
        [property: JsonPropertyName("link")] string Link,
        [property: JsonPropertyName("date")] DateTime Date,
        [property: JsonPropertyName("date_gmt")] DateTime? DateGmt,
        [property: JsonPropertyName("title")] RenderedText? Title,
        [property: JsonPropertyName("excerpt")] RenderedText? Excerpt,
        [property: JsonPropertyName("content")] RenderedText? Content,
        [property: JsonPropertyName("_embedded")] Embedded? Embedded);
}

public sealed record ImportResult(int PostsImported, int PagesImported, int AlreadyImported, IReadOnlyList<string> Skipped);

/// <summary>
/// One-off import of the old WordPress site. Blog posts arrive published with their original dates (they were
/// already public). Pages arrive as drafts, because the old pages have out-of-date details that need checking.
/// Images uploaded to the old site are copied into the church's own storage, so the old site can be switched off;
/// one that can't be copied is listed in <see cref="ImportResult.Skipped"/> and keeps its old address.
/// Running it again skips anything already imported.
/// </summary>
public sealed partial class WordPressImporter(
    IContentDb db,
    IWordPressSource source,
    IPublicImageStore images,
    IOptions<WordPressOptions> options,
    IChurchDirectory church,
    IAuditLog audit,
    TimeProvider clock)
{
    /// <summary>Old address to new, so an image used twice is copied once.</summary>
    private readonly Dictionary<string, string> _copied = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _notCopied = [];

    /// <summary>Old pages the new site replaces with its own (home, sermons, blog list), plus test content.</summary>
    private static readonly HashSet<string> Ignore = new(StringComparer.OrdinalIgnoreCase) { "home", "sermons", "shapers-blog", "test-pod", "sample-page", "hello-world" };

    public async Task<ImportResult> ImportAsync(CancellationToken cancellationToken)
    {
        var scope = ScopePath.Parse((await church.GetRootScopeAsync(cancellationToken)).Path);
        var now = clock.GetUtcNow();
        var skipped = new List<string>();
        var already = 0;
        var postsImported = 0;
        var pagesImported = 0;

        foreach (var item in await source.PostsAsync(cancellationToken))
        {
            var legacy = PathOf(item.Link);
            if (Ignore.Contains(item.Slug))
            {
                skipped.Add(legacy);
                continue;
            }

            if (await db.Posts.AnyAsync(p => p.LegacyPath == legacy, cancellationToken))
            {
                already++;
                continue;
            }

            var title = Text(item.TitleHtml);
            var body = await CopyImagesInAsync(ToMarkdown(item.ContentHtml, await MediaAsync(item.ContentHtml, cancellationToken)), cancellationToken);
            var cover = await CopyAsync(Https(item.FeaturedImageUrl), cancellationToken);
            var post = Post.Create(PostKind.Blog, title, Summary(item.ExcerptHtml), body, scope, now);
            post.Edit(PostKind.Blog, title, Summary(item.ExcerptHtml), body, null, cover, null, now);
            post.UseSlug(await FreeSlugAsync(Slug.IsValid(item.Slug) ? item.Slug : SlugRules.From(title), s => db.Posts.AnyAsync(p => p.Slug == s, cancellationToken)));
            post.SetLegacyPath(legacy);
            post.ImportedAs(item.Date, now);
            db.Posts.Add(post);
            postsImported++;
        }

        foreach (var item in await source.PagesAsync(cancellationToken))
        {
            var legacy = PathOf(item.Link);
            if (Ignore.Contains(item.Slug))
            {
                skipped.Add(legacy);
                continue;
            }

            if (await db.Pages.AnyAsync(p => p.LegacyPath == legacy, cancellationToken))
            {
                already++;
                continue;
            }

            var markdown = await CopyImagesInAsync(ToMarkdown(item.ContentHtml, await MediaAsync(item.ContentHtml, cancellationToken)), cancellationToken);
            if (string.IsNullOrWhiteSpace(markdown))
            {
                skipped.Add(legacy);
                continue;
            }

            var page = Page.Create(Text(item.TitleHtml), Summary(item.ExcerptHtml), markdown, scope, now);
            page.UseSlug(await FreeSlugAsync(Slug.IsValid(item.Slug) ? item.Slug : SlugRules.From(page.Title), s => db.Pages.AnyAsync(p => p.Slug == s, cancellationToken)));
            page.SetLegacyPath(legacy);
            db.Pages.Add(page);
            pagesImported++;
        }

        await db.SaveChangesAsync(cancellationToken);
        skipped.AddRange(_notCopied.Select(path => $"image not copied: {path}"));
        await audit.RecordAsync(
            new AuditRecord("content.wordpress.imported", "content", null, Details: new { postsImported, pagesImported, already, skipped, imagesCopied = _copied.Count }),
            cancellationToken);
        return new ImportResult(postsImported, pagesImported, already, skipped);
    }

    /// <summary>Copies every image from the old site that the Markdown refers to, and points the Markdown at the copies.</summary>
    private async Task<string> CopyImagesInAsync(string markdown, CancellationToken cancellationToken)
    {
        foreach (var url in Upload().Matches(markdown).Select(m => m.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList())
        {
            if (await CopyAsync(url, cancellationToken) is { } copy && copy != url)
            {
                markdown = markdown.Replace(url, copy, StringComparison.Ordinal);
            }
        }

        return markdown;
    }

    /// <summary>The copy's address, or the original when it isn't one of the old site's uploads or can't be copied.</summary>
    private async Task<string?> CopyAsync(string? url, CancellationToken cancellationToken)
    {
        if (url is null || !IsOldSiteUpload(url))
        {
            return url;
        }

        if (_copied.TryGetValue(url, out var copy))
        {
            return copy;
        }

        var file = await source.DownloadAsync(url, cancellationToken);
        if (file is null)
        {
            _notCopied.Add(new Uri(url).AbsolutePath);
            return url;
        }

        try
        {
            using var content = new MemoryStream(file.Content);
            copy = await images.SaveAsync(Path.GetFileName(new Uri(url).AbsolutePath), content, file.ContentType, cancellationToken);
        }
        catch (DomainRuleException)
        {
            _notCopied.Add(new Uri(url).AbsolutePath);
            return url;
        }

        _copied[url] = copy;
        return copy;
    }

    /// <summary>Only files uploaded to the old site are copied; links to anywhere else are left alone.</summary>
    private bool IsOldSiteUpload(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !Uri.TryCreate(options.Value.SiteUrl, UriKind.Absolute, out var site))
        {
            return false;
        }

        var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
        var siteHost = site.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? site.Host[4..] : site.Host;
        return host.Equals(siteHost, StringComparison.OrdinalIgnoreCase) && uri.AbsolutePath.Contains("/wp-content/uploads/", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<IReadOnlyDictionary<int, string>> MediaAsync(string html, CancellationToken cancellationToken)
    {
        var urls = new Dictionary<int, string>();
        foreach (var id in ImageIds(html))
        {
            if (await source.MediaUrlAsync(id, cancellationToken) is { } url)
            {
                urls[id] = (await CopyAsync(Https(url), cancellationToken))!;
            }
        }

        return urls;
    }

    /// <summary>The upload numbers of the page builder's images, which need looking up to get their addresses.</summary>
    public static IReadOnlyList<int> ImageIds(string? html) =>
        BuilderImage().Matches(BuilderCode().Replace(html ?? string.Empty, m => WebUtility.HtmlDecode(m.Value)))
            .Select(m => int.Parse(m.Groups["id"].Value, System.Globalization.CultureInfo.InvariantCulture))
            .Distinct()
            .ToList();

    /// <summary>
    /// WordPress HTML to Markdown, without scripts, players, comments or layout wrappers. The old site's page builder
    /// (WPBakery) leaves codes like [vc_row] in the content: headings and images become real ones, the rest goes.
    /// </summary>
    public static string ToMarkdown(string html, IReadOnlyDictionary<int, string>? media = null)
    {
        // Inside builder codes WordPress encodes the quotes (&#8221;); decode them so the codes can be read.
        var cleaned = BuilderCode().Replace(html ?? string.Empty, m => WebUtility.HtmlDecode(m.Value));
        cleaned = BuilderTextOpen().Replace(cleaned, "<p>");
        cleaned = BuilderTextClose().Replace(cleaned, "</p>");
        cleaned = BuilderHeading().Replace(cleaned, m =>
        {
            var level = m.Groups["tag"].Success ? m.Groups["tag"].Value : "3";
            return $"<h{level}>{m.Groups["text"].Value}</h{level}>";
        });
        cleaned = BuilderImage().Replace(cleaned, m =>
            media is not null && media.TryGetValue(int.Parse(m.Groups["id"].Value, System.Globalization.CultureInfo.InvariantCulture), out var url)
                ? $"<p><img src=\"{WebUtility.HtmlEncode(url)}\" alt=\"\"></p>"
                : string.Empty);
        cleaned = BuilderCode().Replace(cleaned, "\n");
        cleaned = Noise().Replace(cleaned, string.Empty);
        cleaned = Comments().Replace(cleaned, string.Empty);
        cleaned = cleaned.Replace("http://shaperschurch.com", "https://shaperschurch.com", StringComparison.OrdinalIgnoreCase);
        var config = new ReverseMarkdown.Config { GithubFlavored = true };
        config.Tags.Unknown = ReverseMarkdown.Config.UnknownTagsOption.Bypass;
        config.Formatting.RemoveComments = true;
        config.Links.SmartHref = true;
        var converter = new ReverseMarkdown.Converter(config);
        var markdown = converter.Convert(cleaned);
        markdown = BlankLines().Replace(markdown, "\n\n").Trim();
        return markdown.Length > PublishableContent.MaxBody ? markdown[..PublishableContent.MaxBody] : markdown;
    }

    private static string Text(string html) => WebUtility.HtmlDecode(Tags().Replace(html ?? string.Empty, string.Empty)).Trim() is { Length: > 0 } t ? t[..Math.Min(t.Length, PublishableContent.MaxTitle)] : "Untitled";

    private static string? Summary(string html)
    {
        var text = WebUtility.HtmlDecode(Tags().Replace(html ?? string.Empty, " ")).Replace("[&hellip;]", "…", StringComparison.Ordinal).Replace("[…]", "…", StringComparison.Ordinal);
        text = Spaces().Replace(text, " ").Trim();
        if (text.Length == 0)
        {
            return null;
        }

        return text.Length <= PublishableContent.MaxSummary ? text : string.Concat(text.AsSpan(0, PublishableContent.MaxSummary - 1), "…");
    }

    private static string? Https(string? url) =>
        string.IsNullOrWhiteSpace(url) ? null : url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? "https://" + url[7..] : url;

    private static string PathOf(string link) => Uri.TryCreate(link, UriKind.Absolute, out var uri) ? uri.AbsolutePath : link;

    private static async Task<string> FreeSlugAsync(string wanted, Func<string, Task<bool>> taken)
    {
        var slug = wanted;
        for (var n = 2; await taken(slug); n++)
        {
            slug = $"{wanted}-{n}";
        }

        return slug;
    }

    /// <summary>An address of an uploaded file, as it appears in Markdown (ends at a space, bracket or quote).</summary>
    [GeneratedRegex(@"https?://[^\s()\[\]""'<>]+/wp-content/uploads/[^\s()\[\]""'<>]+", RegexOptions.IgnoreCase)]
    private static partial Regex Upload();

    [GeneratedRegex(@"<(script|style|audio|video|iframe|noscript|form)\b[^>]*>.*?</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Noise();

    // Attribute quotes arrive "curled" by WordPress (” or ″), so any quote character is accepted.
    [GeneratedRegex(@"\[vc_custom_heading\b[^\]]*?\btext=[""“”″](?<text>[^""“”″\]]+)[""“”″](?:[^\]]*?tag:h(?<tag>[1-6]))?[^\]]*\]")]
    private static partial Regex BuilderHeading();

    [GeneratedRegex(@"\[vc_single_image\b[^\]]*?\bimage=[""“”″](?<id>\d+)[""“”″][^\]]*\]")]
    private static partial Regex BuilderImage();

    [GeneratedRegex(@"\[/?vc_[a-z_]*[^\]]*\]")]
    private static partial Regex BuilderCode();

    [GeneratedRegex(@"\[vc_column_text\b[^\]]*\]")]
    private static partial Regex BuilderTextOpen();

    [GeneratedRegex(@"\[/vc_column_text\]")]
    private static partial Regex BuilderTextClose();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex Comments();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankLines();
}
