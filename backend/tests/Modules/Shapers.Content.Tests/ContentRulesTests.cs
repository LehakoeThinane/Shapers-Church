using Shapers.Content.Domain;

namespace Shapers.Content.Tests;

public sealed class ContentRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly ScopePath Church = ScopePath.Parse("shapers");

    [Theory]
    [InlineData("About us", "about-us")]
    [InlineData("Shapers Growth Track!", "shapers-growth-track")]
    [InlineData("  Café & Coffee  ", "cafe-coffee")]
    [InlineData("!!!", "untitled")]
    public void Titles_become_readable_addresses(string title, string slug) => Assert.Equal(slug, SlugRules.From(title));

    [Theory]
    [InlineData("about-us", true)]
    [InlineData("About-Us", false)]
    [InlineData("about us", false)]
    [InlineData("-about", false)]
    public void Only_clean_slugs_are_accepted(string slug, bool valid) => Assert.Equal(valid, SlugRules.IsValid(slug));

    [Fact]
    public void Publishing_announces_the_change_once()
    {
        var page = Page.Create("About us", null, "We build productive people.", Church, Now);

        page.Publish(Now);
        page.Publish(Now.AddMinutes(1));

        Assert.Equal(ContentStatus.Published, page.Status);
        Assert.Single(page.DomainEvents.OfType<ContentPublished>());
        Assert.True(page.IsLive(Now));
    }

    [Fact]
    public void Scheduled_content_goes_live_at_its_time()
    {
        var post = Post.Create(PostKind.News, "Baptism Sunday", null, "Sign up at the info desk.", Church, Now);
        post.Schedule(Now.AddHours(2), Now);

        Assert.False(post.PublishIfDue(Now.AddHours(1)));
        Assert.True(post.PublishIfDue(Now.AddHours(2)));
        Assert.Equal(Now.AddHours(2), post.PublishedAt);
        Assert.Throws<DomainRuleException>(() => post.Schedule(Now, Now.AddHours(3)));
    }

    [Fact]
    public void Only_news_has_a_show_until_date()
    {
        var post = Post.Create(PostKind.News, "Baptism Sunday", null, "Body", Church, Now);
        post.Edit(PostKind.Blog, "Baptism Sunday", null, "Body", null, null, new DateOnly(2026, 10, 30), Now);
        Assert.Null(post.ShowUntil);
    }

    [Fact]
    public void Cover_images_need_a_web_address()
    {
        var post = Post.Create(PostKind.Blog, "Anointed to grow", null, "Body", Church, Now);
        Assert.Throws<DomainRuleException>(() => post.Edit(PostKind.Blog, "Anointed to grow", null, "Body", null, "javascript:alert(1)", null, Now));
        post.Edit(PostKind.Blog, "Anointed to grow", null, "Body", null, "https://example.com/cover.jpg", null, Now);
        Assert.Equal("https://example.com/cover.jpg", post.CoverImageUrl);
    }

    [Fact]
    public void Imported_articles_keep_their_original_date_and_do_not_trigger_rebuild_events()
    {
        var post = Post.Create(PostKind.Blog, "Impossible is a lie", null, "Body", Church, Now);
        var original = new DateTimeOffset(2020, 7, 17, 8, 0, 0, TimeSpan.Zero);

        post.ImportedAs(original, Now);

        Assert.Equal(original, post.PublishedAt);
        Assert.Empty(post.DomainEvents);
    }
}
