using Shapers.Content.Domain;

namespace Shapers.Content.Tests;

public sealed class TranslationRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 9, 0, 0, TimeSpan.Zero);
    private static readonly ScopePath Church = ScopePath.Parse("shapers");
    private static readonly Guid Reviewer = Guid.NewGuid();

    private static Post Original()
    {
        var post = Post.Create(PostKind.Blog, "Faith that works", "A summary", "Body", Church, Now);
        post.Edit(PostKind.Blog, "Faith that works", "A summary", "Body", "Pastor Israel", "https://example.com/cover.jpg", null, Now);
        return post;
    }

    [Fact]
    public void A_translation_shares_the_address_and_details_of_its_original()
    {
        var original = Original();

        var zulu = Post.TranslationOf(original, "zu", "Ukholo olusebenzayo", null, "Umbhalo", Now);

        Assert.Equal(original.Slug, zulu.Slug);
        Assert.Equal("zu", zulu.Language);
        Assert.Equal(original.Id, zulu.TranslationOfId);
        Assert.Equal("Pastor Israel", zulu.Author);
        Assert.Equal(ContentStatus.Draft, zulu.Status);
        Assert.False(zulu.TranslationChecked);
    }

    [Fact]
    public void An_unchecked_translation_cannot_go_on_the_site()
    {
        var zulu = Post.TranslationOf(Original(), "zu", "Ukholo", null, "Umbhalo", Now);

        Assert.Throws<DomainRuleException>(() => zulu.Publish(Now));
        Assert.Throws<DomainRuleException>(() => zulu.Schedule(Now.AddDays(1), Now));

        zulu.CheckTranslation(Reviewer, Now, Now);
        zulu.Publish(Now);
        Assert.Equal(ContentStatus.Published, zulu.Status);
    }

    [Fact]
    public void Changing_a_checked_translation_takes_it_off_the_site_until_it_is_checked_again()
    {
        var zulu = Page.TranslationOf(Page.Create("About us", null, "Body", Church, Now), "zu", "Mayelana nathi", null, "Umbhalo", Now);
        zulu.CheckTranslation(Reviewer, Now, Now);
        zulu.Publish(Now);

        zulu.Edit("Mayelana nathi", null, "Umbhalo omusha", null, Now.AddMinutes(5));

        Assert.False(zulu.TranslationChecked);
        Assert.Equal(ContentStatus.Draft, zulu.Status);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("xx")]
    public void Translations_need_another_known_language(string language) =>
        Assert.Throws<DomainRuleException>(() => Post.TranslationOf(Original(), language, "T", null, "B", Now));

    [Fact]
    public void Only_originals_are_translated()
    {
        var zulu = Post.TranslationOf(Original(), "zu", "Ukholo", null, "Umbhalo", Now);

        Assert.Throws<DomainRuleException>(() => Post.TranslationOf(zulu, "st", "Tumelo", null, "Mongolo", Now));
        Assert.Throws<DomainRuleException>(() => Original().CheckTranslation(Reviewer, Now, Now));
    }
}
