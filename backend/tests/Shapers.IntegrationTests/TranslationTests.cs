using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Shapers.Assist.Application;
using Shapers.Assist.Infrastructure;
using Shapers.Content.Application;
using Shapers.Content.Contracts;
using Shapers.Content.Domain;
using Shapers.Identity.Application;
using Shapers.People.Application;

namespace Shapers.IntegrationTests;

public sealed class TranslationTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task A_post_is_translated_checked_by_a_speaker_and_then_readable_in_isiZulu()
    {
        var admin = await api.SignInAdminAsync();
        var post = await (await admin.PostJsonAsync("/api/admin/content/posts",
                new SavePostRequest(PostKind.Blog, "Faith that works", null, null, "Join a cell. Questions? Email info@shaperschurch.com.", "Pastor Israel", null, null)))
            .ReadAsync<PostAdminDto>();
        await (await admin.PostAsync($"/api/admin/content/posts/{post.Id}/publish", null)).ReadAsync<PostAdminDto>();

        // AI drafts the translation. The email address isn't sent, but it's back in the draft.
        var status = await (await admin.GetAsync("/api/admin/assist/status")).ReadAsync<AssistStatusDto>();
        Assert.Contains(status.Languages, l => l.Code == "zu");
        var draft = await (await admin.PostJsonAsync("/api/admin/assist/drafts/translate", new TranslateRequest(ContentType.Post, post.Id, "zu"))).ReadAsync<DraftDto>();
        Assert.DoesNotContain("info@shaperschurch.com", api.Services.GetRequiredService<FakeAiProvider>().LastChat!.User, StringComparison.Ordinal);
        Assert.Contains("info@shaperschurch.com", draft.Translation!.Body, StringComparison.Ordinal);

        var zulu = await (await admin.PostJsonAsync($"/api/admin/content/posts/{post.Id}/translations",
                new CreateTranslationRequest("zu", draft.Translation.Title, draft.Translation.Summary, draft.Translation.Body)))
            .ReadAsync<PostAdminDto>();
        Assert.Equal(post.Slug, zulu.Slug);
        Assert.False(zulu.Translation!.Checked);

        // Not on the site until a speaker of the language checks it.
        var refused = await admin.PostAsync($"/api/admin/content/posts/{zulu.Id}/publish", null);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("content.translation_unchecked", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var translator = await CreateStaffAsync(admin, "translator@test.local", "Translator");
        var queue = await (await translator.GetAsync("/api/admin/content/translations")).ReadAsync<List<TranslationQueueItemDto>>();
        Assert.Contains(queue, q => q.Id == zulu.Id && !q.Checked && q.OriginalTitle == "Faith that works");
        var corrected = await (await translator.PutAsync($"/api/admin/content/posts/{zulu.Id}", Json(
                new SavePostRequest(PostKind.Blog, "Ukholo olusebenzayo", null, null, "Joyina iseli. Imibuzo? Thumela ku-info@shaperschurch.com.", "Pastor Israel", null, null))))
            .ReadAsync<PostAdminDto>();
        Assert.Equal(post.Slug, corrected.Slug);
        await (await translator.PostAsync($"/api/admin/content/posts/{zulu.Id}/check", null)).ReadAsync<PostAdminDto>();
        Assert.Equal(HttpStatusCode.Forbidden, (await translator.PostAsync($"/api/admin/content/posts/{zulu.Id}/publish", null)).StatusCode);
        await (await admin.PostAsync($"/api/admin/content/posts/{zulu.Id}/publish", null)).ReadAsync<PostAdminDto>();

        // The public site lists the English original once, and offers isiZulu on it.
        var anonymous = api.Browser();
        var list = await (await anonymous.GetAsync("/api/content/posts")).ReadAsync<PostPageDto>();
        Assert.Single(list.Items, i => i.Slug == post.Slug);
        var english = await (await anonymous.GetAsync($"/api/content/posts/{post.Slug}")).ReadAsync<PostDto>();
        Assert.Equal(["en", "zu"], english.Languages.Select(l => l.Code));
        var inZulu = await (await anonymous.GetAsync($"/api/content/posts/{post.Slug}?lang=zu")).ReadAsync<PostDto>();
        Assert.Equal("Ukholo olusebenzayo", inZulu.Post.Title);
        Assert.Equal("zu", inZulu.Language);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/content/posts/{post.Slug}?lang=st")).StatusCode);
    }

    [Fact]
    public async Task Only_offered_languages_can_be_drafted_and_one_translation_per_language()
    {
        var admin = await api.SignInAdminAsync();
        var page = await (await admin.PostJsonAsync("/api/admin/content/pages", new SavePageRequest("Our beliefs", null, null, "We believe.", null))).ReadAsync<PageAdminDto>();

        var notOffered = await admin.PostJsonAsync("/api/admin/assist/drafts/translate", new TranslateRequest(ContentType.Page, page.Page.Id, "ve"));
        Assert.Equal(HttpStatusCode.BadRequest, notOffered.StatusCode);

        (await admin.PostJsonAsync($"/api/admin/content/pages/{page.Page.Id}/translations", new CreateTranslationRequest("st", "Seo re se dumelang", null, "Re dumela."))).EnsureSuccessStatusCode();
        var again = await admin.PostJsonAsync($"/api/admin/content/pages/{page.Page.Id}/translations", new CreateTranslationRequest("st", "Again", null, "Again."));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        var original = await (await admin.GetAsync($"/api/admin/content/pages/{page.Page.Id}")).ReadAsync<PageAdminDto>();
        Assert.Equal(["st"], original.Translations.Select(t => t.Language));
    }

    private static StringContent Json(object body) =>
        new(System.Text.Json.JsonSerializer.Serialize(body, ApiFactory.Json), System.Text.Encoding.UTF8, "application/json");

    /// <summary>A staff login holding one built-in role at church level.</summary>
    private async Task<HttpClient> CreateStaffAsync(HttpClient admin, string email, string roleName)
    {
        var person = await (await admin.PostJsonAsync("/api/admin/people", new CreatePersonRequest("Thandi", "Translator", null, null, null, null, null, null, null))).ReadAsync<PersonDetailDto>();
        var setup = await (await admin.PostJsonAsync($"/api/admin/people/{person.Id}/staff-login", new { email })).ReadAsync<StaffLoginSetupDto>();
        const string password = "a-long-enough-password";
        (await admin.PostJsonAsync("/api/auth/staff/set-password", new { setup.UserId, token = setup.SetupToken, password })).EnsureSuccessStatusCode();
        var role = (await (await admin.GetAsync("/api/admin/roles")).ReadAsync<List<RoleDto>>()).Single(r => r.Name == roleName);
        await (await admin.PostJsonAsync("/api/admin/grants", new CreateGrantRequest(person.Id, role.Id, "shapers", null, null))).ReadAsync<GrantDto>();
        return await api.SignInStaffAsync(email, password);
    }
}
