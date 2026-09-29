using System.Net;
using System.Net.Http.Headers;
using Shapers.Identity.Application;
using Shapers.Identity.Infrastructure;
using Shapers.Media.Application;
using Shapers.Media.Domain;
using Shapers.People.Application;
using Shapers.Platform.Web;

namespace Shapers.IntegrationTests;

public sealed class SermonTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task A_sermon_is_hidden_until_published_then_searchable_and_in_the_podcast()
    {
        var admin = await api.SignInAdminAsync();
        var speaker = await CreateSpeakerAsync(admin, "Israel Phiri");
        var draft = await (await admin.PostJsonAsync("/api/admin/media/sermons", Sermon("Behold my servant", speaker.Id, "Isaiah 42:1-9"))).ReadAsync<SermonAdminDto>();
        var anonymous = api.Browser();

        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/media/sermons/{draft.Sermon.Slug}")).StatusCode);
        Assert.Contains("Add audio or a video.", draft.PublishProblems);

        // Upload audio the way the admin portal does: reserve, PUT to the signed URL, confirm, attach.
        var audio = await UploadAudioAsync(admin, 4096);
        await (await admin.PutAsync($"/api/admin/media/sermons/{draft.Sermon.Id}/audio", Json(new { assetId = audio.Id }))).ReadAsync<SermonAdminDto>();
        var published = await (await admin.PostAsync($"/api/admin/media/sermons/{draft.Sermon.Id}/publish", null)).ReadAsync<SermonAdminDto>();
        Assert.Equal(SermonStatus.Published, published.Status);

        var detail = await (await anonymous.GetAsync($"/api/media/sermons/{draft.Sermon.Slug}")).ReadAsync<SermonDetailDto>();
        Assert.Equal(["Isaiah 42:1–9"], detail.Scripture.Select(s => s.Display));
        Assert.Equal(4096, detail.Audio!.SizeBytes);
        var file = await anonymous.GetAsync(detail.Audio.Url);
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Equal(4096, (await file.Content.ReadAsByteArrayAsync()).Length);

        var search = await (await anonymous.GetAsync("/api/media/sermons?Q=servant")).ReadAsync<PagedResult<SermonSummaryDto>>();
        Assert.Contains(search.Items, s => s.Id == draft.Sermon.Id);
        var byBook = await (await anonymous.GetAsync("/api/media/sermons?Book=23")).ReadAsync<PagedResult<SermonSummaryDto>>();
        Assert.Contains(byBook.Items, s => s.Id == draft.Sermon.Id);

        var feed = await anonymous.GetStringAsync("/podcast.xml");
        Assert.StartsWith("<?xml", feed, StringComparison.Ordinal);
        Assert.Contains("<title>Behold my servant</title>", feed, StringComparison.Ordinal);
        Assert.Contains("length=\"4096\"", feed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unreadable_scripture_and_video_links_are_rejected_with_a_helpful_message()
    {
        var admin = await api.SignInAdminAsync();
        var speaker = await CreateSpeakerAsync(admin, "Guest Speaker");

        var badScripture = await admin.PostJsonAsync("/api/admin/media/sermons", Sermon("Typo", speaker.Id, "Hezekiah 3:1"));
        Assert.Equal(HttpStatusCode.BadRequest, badScripture.StatusCode);
        Assert.Contains("Hezekiah 3:1", await badScripture.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var badVideo = await admin.PostJsonAsync("/api/admin/media/sermons", Sermon("Vimeo", speaker.Id, null) with { VideoUrl = "https://vimeo.com/1" });
        Assert.Equal(HttpStatusCode.BadRequest, badVideo.StatusCode);
    }

    [Fact]
    public async Task Editors_without_publish_rights_can_prepare_but_not_publish()
    {
        var admin = await api.SignInAdminAsync();
        var speaker = await CreateSpeakerAsync(admin, "Editor Test");
        var editor = await CreateStaffWithCustomRoleAsync(admin, "sermon.editor@test.local", "Sermon editor", ["media.sermons.edit"]);

        var draft = await (await editor.PostJsonAsync("/api/admin/media/sermons", Sermon("Prepared by an editor", speaker.Id, "John 3:16") with { VideoUrl = "y0jPz7KFw_o" }))
            .ReadAsync<SermonAdminDto>();
        var publish = await editor.PostAsync($"/api/admin/media/sermons/{draft.Sermon.Id}/publish", null);

        Assert.Equal(HttpStatusCode.Forbidden, publish.StatusCode);
    }

    [Fact]
    public async Task Youtube_import_creates_drafts_once_and_reads_scripture_from_titles()
    {
        var admin = await api.SignInAdminAsync();

        var first = await (await admin.PostAsync("/api/admin/media/import/youtube", null)).ReadAsync<ImportResultDto>();
        var second = await (await admin.PostAsync("/api/admin/media/import/youtube", null)).ReadAsync<ImportResultDto>();

        Assert.Equal(2, first.Created);
        Assert.Equal(0, second.Created);
        Assert.Equal(2, second.AlreadyImported);

        var drafts = await (await admin.GetAsync("/api/admin/media/sermons?Q=Deep%20calls&PageSize=50")).ReadAsync<PagedResult<SermonAdminListItemDto>>();
        var imported = Assert.Single(drafts.Items);
        Assert.Equal(SermonStatus.Draft, imported.Status);
        var detail = await (await admin.GetAsync($"/api/admin/media/sermons/{imported.Id}")).ReadAsync<SermonAdminDto>();
        Assert.Equal("Deep calls unto deep", detail.Sermon.Title);
        Assert.Equal(["Psalm 42:1–11"], detail.Sermon.Scripture.Select(s => s.Display));
        Assert.Equal("youtube:y0jPz7KFw_o", detail.ImportSource);
    }

    [Fact]
    public async Task Members_can_keep_their_place_in_a_sermon()
    {
        var admin = await api.SignInAdminAsync();
        var speaker = await CreateSpeakerAsync(admin, "Playback Test");
        var sermon = await (await admin.PostJsonAsync("/api/admin/media/sermons", Sermon("Keep your place", speaker.Id, null))).ReadAsync<SermonAdminDto>();
        var audio = await UploadAudioAsync(admin, 2048, durationSeconds: 1800);
        await (await admin.PutAsync($"/api/admin/media/sermons/{sermon.Sermon.Id}/audio", Json(new { assetId = audio.Id }))).ReadAsync<SermonAdminDto>();
        await (await admin.PostAsync($"/api/admin/media/sermons/{sermon.Sermon.Id}/publish", null)).ReadAsync<SermonAdminDto>();

        var member = await SignInMemberAsync("082 555 0101", "+27825550101");
        (await member.PutAsync($"/api/me/playback/{sermon.Sermon.Id}", Json(new { positionSeconds = 600 }))).EnsureSuccessStatusCode();

        var list = await (await member.GetAsync("/api/me/playback")).ReadAsync<List<ContinueListeningDto>>();
        var entry = Assert.Single(list);
        Assert.Equal(600, entry.PositionSeconds);

        (await member.PutAsync($"/api/me/playback/{sermon.Sermon.Id}", Json(new { positionSeconds = 1790 }))).EnsureSuccessStatusCode();
        Assert.Empty(await (await member.GetAsync("/api/me/playback")).ReadAsync<List<ContinueListeningDto>>());
    }

    private static SaveSermonRequest Sermon(string title, Guid speakerId, string? scripture) =>
        new(title, new DateOnly(2026, 9, 27), null, null, null, [], [speakerId], scripture, null, null);

    private static StringContent Json(object body) =>
        new(System.Text.Json.JsonSerializer.Serialize(body, ApiFactory.Json), System.Text.Encoding.UTF8, "application/json");

    private static async Task<SpeakerDto> CreateSpeakerAsync(HttpClient admin, string name) =>
        await (await admin.PostJsonAsync("/api/admin/media/speakers", new SaveSpeakerRequest(name, null, null, null, null))).ReadAsync<SpeakerDto>();

    private async Task<AssetDto> UploadAudioAsync(HttpClient admin, int size, int? durationSeconds = 2400)
    {
        var start = await (await admin.PostJsonAsync("/api/admin/media/uploads", new StartUploadRequest(MediaKind.Audio, "sermon.mp3", "audio/mpeg", size)))
            .ReadAsync<StartUploadResponse>();
        using var put = new HttpRequestMessage(HttpMethod.Put, start.Upload.Url) { Content = new ByteArrayContent(new byte[size]) };
        foreach (var (header, value) in start.Upload.Headers)
        {
            if (!put.Content.Headers.TryAddWithoutValidation(header, value))
            {
                put.Headers.TryAddWithoutValidation(header, value);
            }
        }

        put.Content.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
        (await admin.SendAsync(put)).EnsureSuccessStatusCode();
        return await (await admin.PostJsonAsync($"/api/admin/media/uploads/{start.AssetId}/complete", new CompleteUploadRequest(durationSeconds))).ReadAsync<AssetDto>();
    }

    private async Task<HttpClient> CreateStaffWithCustomRoleAsync(HttpClient admin, string email, string roleName, string[] permissions)
    {
        var person = await (await admin.PostJsonAsync("/api/admin/people", new CreatePersonRequest("Staff", roleName, null, null, null, null, null, null, null))).ReadAsync<PersonDetailDto>();
        var setup = await (await admin.PostJsonAsync($"/api/admin/people/{person.Id}/staff-login", new { email })).ReadAsync<StaffLoginSetupDto>();
        const string password = "a-long-enough-password";
        (await admin.PostJsonAsync("/api/auth/staff/set-password", new { setup.UserId, token = setup.SetupToken, password })).EnsureSuccessStatusCode();
        var role = await (await admin.PostJsonAsync("/api/admin/roles", new SaveRoleRequest(roleName, null, permissions))).ReadAsync<RoleDto>();
        await (await admin.PostJsonAsync("/api/admin/grants", new CreateGrantRequest(person.Id, role.Id, "shapers", null, null))).ReadAsync<GrantDto>();
        return await api.SignInStaffAsync(email, password);
    }

    private async Task<HttpClient> SignInMemberAsync(string phone, string e164)
    {
        var client = api.Browser();
        var request = await (await client.PostJsonAsync("/api/auth/otp/request", new { phone })).ReadAsync<RequestCodeResponse>();
        var verified = await (await client.PostJsonAsync("/api/auth/otp/verify", new { request.ChallengeId, code = api.Sms.LastCodeFor(e164) })).ReadAsync<SignInResponse>();
        var registered = await (await client.PostJsonAsync("/api/auth/register", new
        {
            request.ChallengeId,
            registrationTicket = verified.RegistrationTicket,
            firstName = "Listener",
            lastName = "Member",
            policyVersion = "2026-09",
            consents = new[] { new { purpose = "processing.church_record", granted = true } },
        })).ReadAsync<SignInResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registered.Tokens!.AccessToken);
        return client;
    }
}
