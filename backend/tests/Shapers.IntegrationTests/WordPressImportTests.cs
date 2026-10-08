using Shapers.Content.Application;
using Shapers.Content.Domain;

namespace Shapers.IntegrationTests;

public sealed class WordPressImportTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Posts_arrive_published_pages_arrive_as_drafts_and_a_second_run_changes_nothing()
    {
        var admin = await api.SignInAdminAsync();

        var first = await (await admin.PostAsync("/api/admin/content/import-wordpress", null)).ReadAsync<ImportResult>();
        Assert.Equal(1, first.PostsImported);
        Assert.Equal(1, first.PagesImported);
        Assert.Contains("/test-pod/", first.Skipped);
        Assert.Contains("/home/", first.Skipped);

        var again = await (await admin.PostAsync("/api/admin/content/import-wordpress", null)).ReadAsync<ImportResult>();
        Assert.Equal(0, again.PostsImported + again.PagesImported);
        Assert.Equal(2, again.AlreadyImported);

        // The article is live at once, with its original date and the old address redirecting to it.
        var post = await (await api.Browser().GetAsync("/api/content/posts/impossible-is-a-lie")).ReadAsync<PostDto>();
        Assert.Equal(new DateTimeOffset(2020, 7, 17, 8, 0, 0, TimeSpan.Zero), post.Post.PublishedAt);
        Assert.Contains("**able**", post.Body, StringComparison.Ordinal);

        // The old site's images are copied into the church's own storage, so the old site can be switched off.
        Assert.Contains("/media-files/imported/", post.Post.CoverImageUrl, StringComparison.Ordinal);
        Assert.EndsWith("/cover.jpg", post.Post.CoverImageUrl, StringComparison.Ordinal);
        Assert.Contains("/media-files/imported/", post.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("uploads/2020/07/inline.jpg", post.Body, StringComparison.Ordinal);
        // One that's gone from the old site keeps its address and is reported; links elsewhere are left alone.
        Assert.Contains("uploads/2020/07/missing.png", post.Body, StringComparison.Ordinal);
        Assert.Contains("image not copied: /wp-content/uploads/2020/07/missing.png", first.Skipped);
        Assert.Contains("https://images.example.org/elsewhere.jpg", post.Body, StringComparison.Ordinal);
        var redirects = await (await api.Browser().GetAsync("/api/content/redirects")).ReadAsync<List<RedirectDto>>();
        Assert.Contains(redirects, r => r.From == "/impossible-is-a-lie/" && r.To == "/blog/impossible-is-a-lie");

        // The page waits for review: not public, and not redirected, until someone publishes it.
        var pages = await (await admin.GetAsync("/api/admin/content/pages")).ReadAsync<List<PageAdminDto>>();
        var about = Assert.Single(pages, p => p.Page.Slug == "about-us");
        Assert.Equal(ContentStatus.Draft, about.Status);
        Assert.Equal("/about-us/", about.LegacyPath);
        Assert.DoesNotContain(redirects, r => r.From == "/about-us/");
    }
}

/// <summary>Stands in for the old WordPress site: one real post, one page, and the things the import skips.</summary>
public sealed class FakeWordPress : IWordPressSource
{
    public Task<IReadOnlyList<WordPressItem>> PostsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<WordPressItem>>(
    [
        new("impossible-is-a-lie", "https://shaperschurch.com/impossible-is-a-lie/", new DateTimeOffset(2020, 7, 17, 8, 0, 0, TimeSpan.Zero),
            "Impossible is a lie", "<p>God is able&hellip; [&hellip;]</p>",
            "<p>God is <strong>able</strong>.</p><script>x()</script>"
                + "<p><img src=\"http://shaperschurch.com/wp-content/uploads/2020/07/inline.jpg\" alt=\"Worship\"></p>"
                + "<p><img src=\"https://shaperschurch.com/wp-content/uploads/2020/07/missing.png\" alt=\"Gone\"></p>"
                + "<p><img src=\"https://images.example.org/elsewhere.jpg\" alt=\"Elsewhere\"></p>",
            "https://shaperschurch.com/wp-content/uploads/2020/07/cover.jpg"),
        new("test-pod", "https://shaperschurch.com/test-pod/", new DateTimeOffset(2020, 12, 8, 11, 40, 0, TimeSpan.Zero), "Test Pod", string.Empty, "<p>test</p>", null),
    ]);

    public Task<string?> MediaUrlAsync(int id, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(id == 148 ? "http://shaperschurch.com/wp-content/uploads/2020/10/snapscan.png" : null);

    /// <summary>The old site's uploads are a tiny PNG; anything ending in "missing.png" is gone from the old site.</summary>
    public Task<DownloadedFile?> DownloadAsync(string url, CancellationToken cancellationToken) =>
        Task.FromResult(url.EndsWith("missing.png", StringComparison.Ordinal) ? null : new DownloadedFile(Png, "image/png"));

    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    public Task<IReadOnlyList<WordPressItem>> PagesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<WordPressItem>>(
    [
        new("about-us", "https://shaperschurch.com/about-us/", DateTimeOffset.UtcNow, "About Us", string.Empty, "<h2>Who we are</h2><p>Shapers Church, 2 Wakis Avenue, Strydom Park.</p>", null),
        new("home", "https://shaperschurch.com/home/", DateTimeOffset.UtcNow, "Home", string.Empty, "<p>Welcome</p>", null),
    ]);
}
