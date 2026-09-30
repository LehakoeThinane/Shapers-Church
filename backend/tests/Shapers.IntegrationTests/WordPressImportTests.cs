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
            "Impossible is a lie", "<p>God is able&hellip; [&hellip;]</p>", "<p>God is <strong>able</strong>.</p><script>x()</script>", null),
        new("test-pod", "https://shaperschurch.com/test-pod/", new DateTimeOffset(2020, 12, 8, 11, 40, 0, TimeSpan.Zero), "Test Pod", string.Empty, "<p>test</p>", null),
    ]);

    public Task<string?> MediaUrlAsync(int id, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(id == 148 ? "http://shaperschurch.com/wp-content/uploads/2020/10/snapscan.png" : null);

    public Task<IReadOnlyList<WordPressItem>> PagesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<WordPressItem>>(
    [
        new("about-us", "https://shaperschurch.com/about-us/", DateTimeOffset.UtcNow, "About Us", string.Empty, "<h2>Who we are</h2><p>Shapers Church, 2 Wakis Avenue, Strydom Park.</p>", null),
        new("home", "https://shaperschurch.com/home/", DateTimeOffset.UtcNow, "Home", string.Empty, "<p>Welcome</p>", null),
    ]);
}
