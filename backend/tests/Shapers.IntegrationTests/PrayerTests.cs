using System.Net;
using System.Net.Http.Headers;
using Shapers.Identity.Application;
using Shapers.People.Application;
using Shapers.People.Contracts;
using Shapers.People.Domain;
using Shapers.Prayer.Application;
using Shapers.Prayer.Domain;

namespace Shapers.IntegrationTests;

public sealed class PrayerTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static readonly ConsentDecision[] Consents = [new(ConsentPurposes.ChurchRecord, true)];

    [Fact]
    public async Task A_wall_request_appears_only_after_review_with_the_reviewed_wording_and_first_name()
    {
        var admin = await api.SignInAdminAsync();
        var asker = await SignInMemberAsync("082 555 2001", "+27825552001", "Lindiwe", "Sithole");
        var friend = await SignInMemberAsync("082 555 2002", "+27825552002", "Kagiso", "Mahlangu");

        var mine = await (await asker.PostJsonAsync("/api/prayer/requests", new SubmitPrayerRequest("Pray for my brother Sipho's job interview on Monday.", true, false, true)))
            .ReadAsync<MyPrayerRequestDto>();
        Assert.Equal(PrayerStatus.AwaitingReview, mine.Status);
        Assert.DoesNotContain(await WallAsync(friend), w => w.Id == mine.Id);

        var queue = await (await admin.GetAsync("/api/admin/prayer/review")).ReadAsync<List<PrayerAdminDto>>();
        Assert.Contains(queue, q => q.Id == mine.Id && q.PersonName == "Lindiwe Sithole");
        (await admin.PostJsonAsync($"/api/admin/prayer/{mine.Id}/approve", new ReviewRequest("Pray for my brother's job interview on Monday.", null))).EnsureSuccessStatusCode();

        var shown = Assert.Single(await WallAsync(friend), w => w.Id == mine.Id);
        Assert.Equal("Lindiwe", shown.Name);
        Assert.Equal("Pray for my brother's job interview on Monday.", shown.Text);
        Assert.False(shown.IsMine);

        // "I prayed" counts once per person, however often it is tapped.
        Assert.Equal(1, await (await friend.PostAsync($"/api/prayer/requests/{mine.Id}/prayed", null)).ReadAsync<int>());
        Assert.Equal(1, await (await friend.PostAsync($"/api/prayer/requests/{mine.Id}/prayed", null)).ReadAsync<int>());
        Assert.True(Assert.Single(await WallAsync(friend), w => w.Id == mine.Id).IPrayed);

        // The requester sees their original words, the count, and can mark it answered.
        var answered = await (await asker.PostJsonAsync($"/api/me/prayer-requests/{mine.Id}/answered", new AnswerRequest("He got the job!"))).ReadAsync<MyPrayerRequestDto>();
        Assert.Equal(1, answered.PrayedCount);
        Assert.Equal("Pray for my brother Sipho's job interview on Monday.", answered.Text);
        Assert.True(Assert.Single(await WallAsync(friend), w => w.Id == mine.Id).Answered);

        // Withdrawing takes it off the wall.
        (await asker.PostAsync($"/api/me/prayer-requests/{mine.Id}/withdraw", null)).EnsureSuccessStatusCode();
        Assert.DoesNotContain(await WallAsync(friend), w => w.Id == mine.Id);
    }

    [Fact]
    public async Task Pastors_only_and_anonymous_requests_protect_the_requester()
    {
        var admin = await api.SignInAdminAsync();
        var asker = await SignInMemberAsync("082 555 2003", "+27825552003", "Nomsa", "Dube");
        var friend = await SignInMemberAsync("082 555 2004", "+27825552004", "Pieter", "Botha");

        var private_ = await (await asker.PostJsonAsync("/api/prayer/requests", new SubmitPrayerRequest("Struggling in my marriage.", false, false, true))).ReadAsync<MyPrayerRequestDto>();
        Assert.Equal(PrayerStatus.WithPastors, private_.Status);
        var anonymous = await (await asker.PostJsonAsync("/api/prayer/requests", new SubmitPrayerRequest("Pray for healing.", true, true, true))).ReadAsync<MyPrayerRequestDto>();

        // Pastors-only requests never reach the review queue, so they can't be approved onto the wall.
        var queue = await (await admin.GetAsync("/api/admin/prayer/review")).ReadAsync<List<PrayerAdminDto>>();
        Assert.DoesNotContain(queue, q => q.Id == private_.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostJsonAsync($"/api/admin/prayer/{private_.Id}/approve", new ReviewRequest(null, null))).StatusCode);

        (await admin.PostJsonAsync($"/api/admin/prayer/{anonymous.Id}/approve", new ReviewRequest(null, null))).EnsureSuccessStatusCode();
        var wall = await WallAsync(friend);
        Assert.DoesNotContain(wall, w => w.Id == private_.Id);
        var shown = Assert.Single(wall, w => w.Id == anonymous.Id);
        Assert.Equal("Someone from Shapers", shown.Name);

        // Pastors still see who asked, and every read is recorded in the audit log.
        var all = await (await admin.GetAsync("/api/admin/prayer")).ReadAsync<List<PrayerAdminDto>>();
        Assert.Equal("Nomsa Dube", Assert.Single(all, a => a.Id == anonymous.Id).PersonName);
        Assert.Contains(all, a => a.Id == private_.Id);

        // Members can't reach the admin lists.
        Assert.Equal(HttpStatusCode.Forbidden, (await friend.GetAsync("/api/admin/prayer")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await friend.GetAsync("/api/admin/prayer/review")).StatusCode);
    }

    [Fact]
    public async Task Requests_need_consent_and_a_signed_in_member()
    {
        var anonymous = api.Browser();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/prayer/wall")).StatusCode);

        var member = await SignInMemberAsync("082 555 2005", "+27825552005", "Ayanda", "Zulu");
        var refused = await member.PostJsonAsync("/api/prayer/requests", new SubmitPrayerRequest("Pray for me.", true, false, false));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("prayer.consent_required", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_prayer_request_on_a_connect_card_is_filed_with_the_pastors()
    {
        var admin = await api.SignInAdminAsync();
        var guest = api.Browser();
        (await guest.PostJsonAsync("/api/connect", new ConnectCardRequest(
            "Refilwe", "Nkosi", null, "refilwe@example.com", [ConnectReason.FirstTime, ConnectReason.Prayer], "Please pray for my new job.", "website", null, true, "2026-09")))
            .EnsureSuccessStatusCode();

        PrayerAdminDto? filed = null;
        for (var attempt = 0; attempt < 40 && filed is null; attempt++)
        {
            var all = await (await admin.GetAsync("/api/admin/prayer")).ReadAsync<List<PrayerAdminDto>>();
            filed = all.FirstOrDefault(a => a.Text == "Please pray for my new job.");
            if (filed is null)
            {
                await Task.Delay(250, TestContext.Current.CancellationToken);
            }
        }

        Assert.NotNull(filed);
        Assert.Equal(PrayerVisibility.PastorsOnly, filed.Visibility);
        Assert.Equal(PrayerSource.ConnectCard, filed.Source);
        Assert.Equal("Refilwe Nkosi", filed.PersonName);
    }

    private static async Task<List<PrayerWallItemDto>> WallAsync(HttpClient client) =>
        await (await client.GetAsync("/api/prayer/wall")).ReadAsync<List<PrayerWallItemDto>>();

    private async Task<HttpClient> SignInMemberAsync(string phone, string e164, string first, string last)
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
        return client;
    }
}
