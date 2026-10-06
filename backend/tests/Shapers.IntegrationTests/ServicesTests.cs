using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Shapers.Identity.Application;
using Shapers.People.Application;
using Shapers.People.Contracts;
using Shapers.People.Domain;
using Shapers.Services.Application;
using Shapers.Services.Domain;

namespace Shapers.IntegrationTests;

public sealed class ServicesTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static readonly ConsentDecision[] Consents = [new(ConsentPurposes.ChurchRecord, true)];
    private static readonly DateOnly NextSunday = NextDay(DayOfWeek.Sunday);

    [Fact]
    public async Task A_service_is_planned_from_a_template_people_are_scheduled_and_answer_and_the_band_rehearses()
    {
        var admin = await api.SignInAdminAsync();
        var (member, memberId) = await SignInMemberAsync("082 555 7001", "+27825557001", "Thabo", "Keys");

        // Team, positions and a member.
        var team = await (await admin.PostJsonAsync("/api/admin/services/teams", new SaveTeamRequest("Worship", null, false, null))).ReadAsync<TeamDto>();
        team = await (await admin.PostJsonAsync($"/api/admin/services/teams/{team.Id}/positions", new SavePositionRequest("Keys", 1))).ReadAsync<TeamDto>();
        team = await (await admin.PostJsonAsync($"/api/admin/services/teams/{team.Id}/positions", new SavePositionRequest("Vocals", 2))).ReadAsync<TeamDto>();
        var keys = team.Positions.Single(p => p.Name == "Keys").Id;
        team = await (await admin.PutAsync($"/api/admin/services/teams/{team.Id}/members", Json(new SaveMemberRequest(memberId, [keys], false)))).ReadAsync<TeamDto>();
        Assert.Equal("Thabo Keys", Assert.Single(team.Members).Name);

        // A song and a template with it; a plan from the template.
        var song = await (await admin.PostJsonAsync("/api/admin/services/songs", new SaveSongRequest("Way Maker", "Sinach", "7115744", ["Faith"], "You are here, moving in our midst", null,
            [new Arrangement(Guid.Empty, "Default", "E", 68, "https://files.example/way-maker.pdf", "way-maker.pdf", null, null)]))).ReadAsync<SongDto>();
        var type = await (await admin.PostJsonAsync("/api/admin/services/types", new SaveServiceTypeRequest("Sunday 09:00", new TimeOnly(9, 0),
            [
                new PlanItem(Guid.Empty, PlanItemKind.Header, "Worship", 0, null, null, null, null, null),
                new PlanItem(Guid.Empty, PlanItemKind.Song, "Way Maker", 360, null, song.Id, song.Arrangements[0].Id, "E", null),
                new PlanItem(Guid.Empty, PlanItemKind.Item, "Sermon", 2400, null, null, null, null, "Pastor Israel"),
            ],
            [new PositionNeed(keys, 1)], null))).ReadAsync<ServiceTypeDto>();
        var plan = await (await admin.PostJsonAsync("/api/admin/services/plans", new CreatePlanRequest(type.Id, NextSunday, null, null, null))).ReadAsync<PlanDto>();
        Assert.Equal(new TimeOnly(9, 46), plan.EndTime);
        Assert.Equal(new TimeOnly(9, 6), plan.Items.Single(i => i.Item.Title == "Sermon").StartsAt);
        Assert.Equal("Way Maker", plan.Items.Single(i => i.Item.Kind == PlanItemKind.Song).SongTitle);

        // The member is free; schedule them.
        var candidates = await (await admin.GetAsync($"/api/admin/services/plans/{plan.Id}/candidates?positionId={keys}")).ReadAsync<List<CandidateDto>>();
        Assert.True(Assert.Single(candidates).Available);
        plan = await (await admin.PostJsonAsync($"/api/admin/services/plans/{plan.Id}/assignments", new AssignRequest(keys, memberId))).ReadAsync<PlanDto>();
        var need = Assert.Single(plan.Needs);
        Assert.Equal((1, 0, 1), (need.Needed, need.Filled, need.Pending));
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostJsonAsync($"/api/admin/services/plans/{plan.Id}/assignments", new AssignRequest(keys, memberId))).StatusCode);

        // They see it in the app and say yes.
        var mine = await (await member.GetAsync("/api/me/serving")).ReadAsync<List<MyAssignmentDto>>();
        var request = Assert.Single(mine);
        Assert.Equal(("Keys", "Worship", AssignmentStatus.Pending), (request.Position, request.Team, request.Status));
        var answered = await (await member.PostJsonAsync($"/api/me/serving/{request.Id}/answer", new ServingAnswerRequest(true, null))).ReadAsync<MyAssignmentDto>();
        Assert.Equal(AssignmentStatus.Accepted, answered.Status);

        // The band rehearses: the song in the planned key with its chart and lyrics.
        var rehearse = await (await member.GetAsync($"/api/services/plans/{plan.Id}/rehearse")).ReadAsync<RehearsePlanDto>();
        var rehearsed = Assert.Single(rehearse.Songs);
        Assert.Equal(("E", "https://files.example/way-maker.pdf"), (rehearsed.Key, rehearsed.ChartUrl));
        Assert.Contains("moving in our midst", rehearsed.Lyrics, StringComparison.Ordinal);

        // The run sheet: the planner starts it; the member follows it.
        await (await admin.PostJsonAsync($"/api/admin/services/plans/{plan.Id}/live/start", new GoLiveRequest(null))).ReadAsync<LiveDto>();
        var live = await (await member.GetAsync($"/api/services/plans/{plan.Id}/live")).ReadAsync<LiveDto>();
        Assert.True(live.IsLive);
        Assert.Equal("Way Maker", live.Items.Single(i => i.Item.Id == live.CurrentItemId).Item.Title);

        // The matrix and the CCLI report.
        var matrix = await (await admin.GetAsync($"/api/admin/services/matrix?from={NextSunday.AddDays(-1):yyyy-MM-dd}&weeks=2")).ReadAsync<MatrixDto>();
        var cell = Assert.Single(matrix.Cells, c => c.PlanId == plan.Id);
        Assert.Equal(AssignmentStatus.Accepted, Assert.Single(cell.People).Status);
        var report = await (await admin.GetAsync($"/api/admin/services/songs/report?from={NextSunday:yyyy-MM-dd}&to={NextSunday:yyyy-MM-dd}")).ReadAsync<List<SongUsageDto>>();
        Assert.Equal(("7115744", 1), (Assert.Single(report).CcliNumber, report[0].Times));

        // Someone not on the plan can't see it.
        var (stranger, _) = await SignInMemberAsync("082 555 7002", "+27825557002", "Not", "Serving");
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/services/plans/{plan.Id}/rehearse")).StatusCode);
    }

    [Fact]
    public async Task People_answer_from_the_email_link_and_away_dates_block_scheduling()
    {
        var admin = await api.SignInAdminAsync();
        var (member, memberId) = await SignInMemberAsync("082 555 7003", "+27825557003", "Lindiwe", "Sound");
        var team = await (await admin.PostJsonAsync("/api/admin/services/teams", new SaveTeamRequest("Production", null, false, null))).ReadAsync<TeamDto>();
        team = await (await admin.PostJsonAsync($"/api/admin/services/teams/{team.Id}/positions", new SavePositionRequest("Sound", 1))).ReadAsync<TeamDto>();
        var sound = team.Positions.Single().Id;
        await (await admin.PutAsync($"/api/admin/services/teams/{team.Id}/members", Json(new SaveMemberRequest(memberId, [sound], false)))).ReadAsync<TeamDto>();
        var plan = await (await admin.PostJsonAsync("/api/admin/services/plans", new CreatePlanRequest(null, NextSunday, "Sunday 09:00", new TimeOnly(9, 0), null))).ReadAsync<PlanDto>();
        plan = await (await admin.PostJsonAsync($"/api/admin/services/plans/{plan.Id}/assignments", new AssignRequest(sound, memberId))).ReadAsync<PlanDto>();
        var assignmentId = Assert.Single(plan.Assignments).Id;

        // The email link opens a page; only the button changes anything.
        var token = api.Services.GetRequiredService<IAnswerLinks>().Token(assignmentId);
        var anonymous = api.Browser();
        var page = await anonymous.GetAsync($"/api/services/answer?token={Uri.EscapeDataString(token)}");
        Assert.Contains("Yes, I can serve", await page.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.GetAsync("/api/services/answer?token=forged")).StatusCode);
        var no = await anonymous.PostAsync("/api/services/answer", new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token, ["answer"] = "no", ["reason"] = "Family wedding" }));
        Assert.Equal(HttpStatusCode.OK, no.StatusCode);
        var updated = await (await admin.GetAsync($"/api/admin/services/plans/{plan.Id}")).ReadAsync<PlanDto>();
        Assert.Equal((AssignmentStatus.Declined, "Family wedding"), (updated.Assignments[0].Status, updated.Assignments[0].DeclineReason));

        // Away the Sunday after: they show as away and can't be scheduled.
        var later = NextSunday.AddDays(7);
        (await member.PostJsonAsync("/api/me/serving/blockouts", new SaveBlockoutRequest(later, later, "Holiday"))).EnsureSuccessStatusCode();
        var second = await (await admin.PostJsonAsync("/api/admin/services/plans", new CreatePlanRequest(null, later, "Sunday 09:00", null, null))).ReadAsync<PlanDto>();
        var candidates = await (await admin.GetAsync($"/api/admin/services/plans/{second.Id}/candidates?positionId={sound}")).ReadAsync<List<CandidateDto>>();
        Assert.Equal("Away that day", Assert.Single(candidates).Reason);
        var refused = await admin.PostJsonAsync($"/api/admin/services/plans/{second.Id}/assignments", new AssignRequest(sound, memberId));
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
    }

    private static DateOnly NextDay(DayOfWeek day)
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(2)).AddDays(1);
        while (date.DayOfWeek != day)
        {
            date = date.AddDays(1);
        }

        return date;
    }

    private static StringContent Json(object body) =>
        new(System.Text.Json.JsonSerializer.Serialize(body, ApiFactory.Json), System.Text.Encoding.UTF8, "application/json");

    private async Task<(HttpClient Client, Guid PersonId)> SignInMemberAsync(string phone, string e164, string first, string last)
    {
        var client = api.Browser();
        var request = await (await client.PostJsonAsync("/api/auth/otp/request", new { phone })).ReadAsync<RequestCodeResponse>();
        var verified = await (await client.PostJsonAsync("/api/auth/otp/verify", new { request.ChallengeId, code = api.Sms.LastCodeFor(e164) })).ReadAsync<SignInResponse>();
        var signedIn = await (await client.PostJsonAsync("/api/auth/register", new
        {
            request.ChallengeId,
            registrationTicket = verified.RegistrationTicket,
            firstName = first,
            lastName = last,
            policyVersion = "2026-09",
            consents = Consents,
        })).ReadAsync<SignInResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", signedIn.Tokens!.AccessToken);
        var profile = await (await client.GetAsync("/api/me/profile")).ReadAsync<PersonDetailDto>();
        return (client, profile.Id);
    }
}
