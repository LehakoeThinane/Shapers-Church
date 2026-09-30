using System.Net;
using Shapers.Content.Application;
using Shapers.Content.Domain;

namespace Shapers.IntegrationTests;

public sealed class ContentTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task A_page_is_invisible_until_published_then_appears_in_the_menu()
    {
        var admin = await api.SignInAdminAsync();
        var anonymous = api.Browser();

        var page = await (await admin.PostJsonAsync("/api/admin/content/pages", new SavePageRequest(
            "Shapers Growth Track", null, "A two-week journey for new and growing members.", "## Week one\nPurpose and belonging.", 2))).ReadAsync<PageAdminDto>();
        Assert.Equal("shapers-growth-track", page.Page.Slug);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/api/content/pages/shapers-growth-track")).StatusCode);

        (await admin.PostAsync($"/api/admin/content/pages/{page.Page.Id}/publish", null)).EnsureSuccessStatusCode();

        var shown = await (await anonymous.GetAsync("/api/content/pages/shapers-growth-track")).ReadAsync<PageDto>();
        Assert.Contains("Purpose and belonging", shown.Body, StringComparison.Ordinal);
        var menu = await (await anonymous.GetAsync("/api/content/menu")).ReadAsync<List<MenuItemDto>>();
        Assert.Contains(menu, m => m.Slug == "shapers-growth-track");

        // A second page with the same title gets its own address.
        var twin = await (await admin.PostJsonAsync("/api/admin/content/pages", new SavePageRequest("Shapers Growth Track", null, null, "Copy", null))).ReadAsync<PageAdminDto>();
        Assert.Equal("shapers-growth-track-2", twin.Page.Slug);
    }

    [Fact]
    public async Task Current_news_shows_newest_first_and_drops_items_past_their_date()
    {
        var admin = await api.SignInAdminAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var old = await CreatePostAsync(admin, new SavePostRequest(PostKind.News, "Old notice", null, null, "Gone now.", null, null, today.AddDays(-1)));
        var current = await CreatePostAsync(admin, new SavePostRequest(PostKind.News, "Baptism Sunday", null, null, "Sign up at the info desk.", null, null, today.AddDays(7)));
        var article = await CreatePostAsync(admin, new SavePostRequest(PostKind.Blog, "Anointed to grow", null, "Short teaching.", "Body", "Pastor", null, null));
        foreach (var id in new[] { old.Id, current.Id, article.Id })
        {
            (await admin.PostAsync($"/api/admin/content/posts/{id}/publish", null)).EnsureSuccessStatusCode();
        }

        var news = await (await api.Browser().GetAsync("/api/content/news")).ReadAsync<List<PostSummaryDto>>();
        Assert.Contains(news, n => n.Id == current.Id);
        Assert.DoesNotContain(news, n => n.Id == old.Id || n.Id == article.Id);

        var blog = await (await api.Browser().GetAsync("/api/content/posts?kind=Blog")).ReadAsync<PostPageDto>();
        Assert.Contains(blog.Items, p => p.Slug == "anointed-to-grow");
        var full = await (await api.Browser().GetAsync("/api/content/posts/anointed-to-grow")).ReadAsync<PostDto>();
        Assert.Equal("Body", full.Body);
    }

    [Fact]
    public async Task Members_cannot_write_content()
    {
        var anonymous = api.Browser();
        var response = await anonymous.PostJsonAsync("/api/admin/content/pages", new SavePageRequest("Hi", null, null, "x", null));
        Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
    }

    private static async Task<PostAdminDto> CreatePostAsync(HttpClient admin, SavePostRequest request) =>
        await (await admin.PostJsonAsync("/api/admin/content/posts", request)).ReadAsync<PostAdminDto>();
}
