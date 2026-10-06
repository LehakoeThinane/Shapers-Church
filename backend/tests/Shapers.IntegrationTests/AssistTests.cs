using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Shapers.Assist.Application;
using Shapers.Assist.Domain;
using Shapers.Assist.Infrastructure;
using Shapers.Church.Application;
using Shapers.Groups.Application;
using Shapers.Groups.Domain;
using Shapers.Identity.Application;
using Shapers.Media.Application;
using Shapers.Media.Domain;
using Shapers.People.Application;

namespace Shapers.IntegrationTests;

public sealed class AssistTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private const string Transcript = "Good morning church. Faith without works is dead. If you need prayer, call the office on 011 234 5678 or email info@shaperschurch.com.";

    [Fact]
    public async Task A_pastor_turns_a_sermon_into_a_church_lesson_that_cell_leaders_see()
    {
        var admin = await api.SignInAdminAsync();
        var sermon = await CreateSermonAsync(admin, "Faith that works");
        await (await admin.PutAsync($"/api/admin/media/sermons/{sermon.Sermon.Id}/transcript", Json(new SetTranscriptRequest(Transcript)))).ReadAsync<SermonAdminDto>();

        var draft = await (await admin.PostJsonAsync("/api/admin/assist/drafts/sermon-lesson", new SermonDraftRequest(sermon.Sermon.Id))).ReadAsync<DraftDto>();

        Assert.Equal(DraftStatus.Pending, draft.Status);
        Assert.NotNull(draft.Lesson);
        Assert.Contains("1. ", draft.Lesson.Body, StringComparison.Ordinal);

        // What went to the AI service: the sermon's words, without the office's phone number and email address.
        var sent = api.Services.GetRequiredService<FakeAiProvider>().LastChat!;
        Assert.Contains("Faith without works is dead.", sent.User, StringComparison.Ordinal);
        Assert.DoesNotContain("011 234 5678", sent.User, StringComparison.Ordinal);
        Assert.DoesNotContain("info@shaperschurch.com", sent.User, StringComparison.Ordinal);

        // The pastor edits the draft into a church lesson, then marks the draft as used.
        var lesson = await (await admin.PostJsonAsync("/api/admin/cells/lessons", new SaveLessonRequest(draft.Lesson.Title, draft.Lesson.Body + "\n\nEdited by the pastor.", null, null, true, sermon.Sermon.Id, null)))
            .ReadAsync<MaterialDto>();
        var accepted = await (await admin.PostAsync($"/api/admin/assist/drafts/{draft.Id}/accept", null)).ReadAsync<DraftDto>();
        Assert.Equal(DraftStatus.Accepted, accepted.Status);
        Assert.True(lesson.IsChurchLesson);
        Assert.Equal("All cells", lesson.CellName);

        // A leader of a cell in the church sees it among the church lessons; their members see it because it's shared.
        var me = await (await admin.GetAsync("/api/me/access")).ReadAsync<JsonElement>();
        var cell = await CreateCellLedByAsync(admin, me.GetProperty("personId").GetGuid());
        var lessons = await (await admin.GetAsync($"/api/cells/{cell.Id}/lessons")).ReadAsync<List<MaterialDto>>();
        Assert.Contains(lessons, l => l.Id == lesson.Id && l.SermonId == sermon.Sermon.Id);
        var mine = await (await admin.GetAsync("/api/me/cells")).ReadAsync<List<MyCellDto>>();
        Assert.Contains(mine.Single(c => c.Id == cell.Id).SharedMaterials, m => m.Id == lesson.Id);

        var history = await (await admin.GetAsync($"/api/admin/assist/drafts?sourceType=sermon&sourceId={sermon.Sermon.Id}")).ReadAsync<List<DraftDto>>();
        Assert.Contains(history, d => d.Id == draft.Id && d.RequestedByMe);
    }

    [Fact]
    public async Task Show_notes_need_a_transcript_first()
    {
        var admin = await api.SignInAdminAsync();
        var sermon = await CreateSermonAsync(admin, "No words yet");

        var response = await admin.PostJsonAsync("/api/admin/assist/drafts/sermon-notes", new SermonDraftRequest(sermon.Sermon.Id));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("assist.no_transcript", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task New_sermon_audio_is_transcribed_in_the_background()
    {
        var admin = await api.SignInAdminAsync();
        var sermon = await CreateSermonAsync(admin, "Transcribe me");
        var audio = await UploadAudioAsync(admin, 1024);

        var queued = await (await admin.PutAsync($"/api/admin/media/sermons/{sermon.Sermon.Id}/audio", Json(new { assetId = audio.Id }))).ReadAsync<SermonAdminDto>();
        Assert.Equal(TranscriptStatus.Queued, queued.Transcript.Status);

        await using (var scope = api.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<TranscriptionJob>().RunAsync(CancellationToken.None);
        }

        var transcript = await (await admin.GetAsync($"/api/admin/media/sermons/{sermon.Sermon.Id}/transcript")).ReadAsync<TranscriptDto>();
        Assert.Equal(TranscriptStatus.Ready, transcript.Info.Status);
        Assert.Equal(TranscriptSource.Audio, transcript.Info.Source);
        Assert.Contains("Faith without works is dead.", transcript.Text!, StringComparison.Ordinal);

        // Drafting now works from what was transcribed.
        var notes = await (await admin.PostJsonAsync("/api/admin/assist/drafts/sermon-notes", new SermonDraftRequest(sermon.Sermon.Id))).ReadAsync<DraftDto>();
        Assert.NotNull(notes.Notes);
        Assert.Contains("Faith", notes.Notes.Topics);
    }

    [Fact]
    public async Task Rewrites_refuse_contact_details()
    {
        var admin = await api.SignInAdminAsync();

        var refused = await admin.PostJsonAsync("/api/admin/assist/drafts/rewrite", new RewriteRequest("Contact Sipho on 082 555 1234 to book.", RewriteMode.Tidy));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("assist.personal_data", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var rewrite = await (await admin.PostJsonAsync("/api/admin/assist/drafts/rewrite", new RewriteRequest("join us sunday 9am", RewriteMode.Tidy))).ReadAsync<DraftDto>();
        Assert.False(string.IsNullOrWhiteSpace(rewrite.Rewrite!.Text));
    }

    [Fact]
    public async Task Staff_without_ai_permission_are_refused_and_see_no_ai_buttons()
    {
        var admin = await api.SignInAdminAsync();
        var editor = await CreateStaffWithCustomRoleAsync(admin, "no.ai@test.local", "Sermons only", ["media.sermons.edit"]);

        var status = await (await editor.GetAsync("/api/admin/assist/status")).ReadAsync<AssistStatusDto>();
        var refused = await editor.PostJsonAsync("/api/admin/assist/drafts/rewrite", new RewriteRequest("Hello", RewriteMode.Tidy));

        Assert.True(status.Enabled);
        Assert.False(status.CanDraft);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await editor.GetAsync("/api/admin/assist/usage")).StatusCode);
    }

    [Fact]
    public async Task Calls_stop_when_the_monthly_budget_is_spent()
    {
        var admin = await api.SignInAdminAsync();
        await using (var scope = api.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AssistDbContext>();
            db.Usage.Add(AiUsage.Record(AiOperation.Chat, "test-spend", "fake", 0, 0, 0, 1_000_000m, null, true, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        try
        {
            var refused = await admin.PostJsonAsync("/api/admin/assist/drafts/rewrite", new RewriteRequest("join us sunday", RewriteMode.Tidy));
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            Assert.Contains("assist.budget_reached", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            var usage = await (await admin.GetAsync("/api/admin/assist/usage")).ReadAsync<UsageDto>();
            Assert.True(usage.SpentThisMonthZar >= 1_000_000m);
            Assert.Contains(usage.Recent, u => u.Purpose == "test-spend");
            Assert.True((await (await admin.GetAsync("/api/admin/assist/status")).ReadAsync<AssistStatusDto>()).BudgetReached);
        }
        finally
        {
            await using var scope = api.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AssistDbContext>();
            db.Usage.RemoveRange(db.Usage.Where(u => u.Purpose == "test-spend"));
            await db.SaveChangesAsync();
        }
    }

    private static StringContent Json(object body) =>
        new(JsonSerializer.Serialize(body, ApiFactory.Json), System.Text.Encoding.UTF8, "application/json");

    private static async Task<SermonAdminDto> CreateSermonAsync(HttpClient admin, string title)
    {
        var speaker = await (await admin.PostJsonAsync("/api/admin/media/speakers", new SaveSpeakerRequest($"Speaker {Guid.NewGuid():N}"[..20], null, null, null, null))).ReadAsync<SpeakerDto>();
        return await (await admin.PostJsonAsync("/api/admin/media/sermons", new SaveSermonRequest(title, new DateOnly(2026, 10, 4), null, null, null, [], [speaker.Id], "James 2:14-17", null, null)))
            .ReadAsync<SermonAdminDto>();
    }

    private static async Task<CellDetailDto> CreateCellLedByAsync(HttpClient admin, Guid leaderId)
    {
        var rivonia = (await (await admin.GetAsync("/api/admin/campuses")).ReadAsync<List<CampusDto>>()).Single(c => c.IsPrimary);
        var cell = await (await admin.PostJsonAsync("/api/admin/cells", new SaveCellRequest($"Lesson cell {Guid.NewGuid():N}"[..20], rivonia.Id, DayOfWeek.Tuesday, new TimeOnly(19, 0), null, null)))
            .ReadAsync<CellDetailDto>();
        return await (await admin.PostJsonAsync($"/api/admin/cells/{cell.Id}/members", new AddMemberRequest(leaderId, CellRole.Leader))).ReadAsync<CellDetailDto>();
    }

    private async Task<AssetDto> UploadAudioAsync(HttpClient admin, int size)
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
        return await (await admin.PostJsonAsync($"/api/admin/media/uploads/{start.AssetId}/complete", new CompleteUploadRequest(1800))).ReadAsync<AssetDto>();
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
}
