using System.Net;
using System.Net.Http.Headers;
using Shapers.Identity.Application;
using Shapers.Media.Application;
using Shapers.Media.Domain;
using Shapers.People.Application;
using Shapers.People.Domain;

namespace Shapers.IntegrationTests;

public sealed class LivestreamTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task A_service_goes_from_upcoming_to_live_with_scripture_on_screen_then_becomes_a_sermon()
    {
        var admin = await api.SignInAdminAsync();
        var anonymous = api.Browser();
        var start = DateTimeOffset.UtcNow.AddMinutes(30);

        var stream = await (await admin.PostJsonAsync("/api/admin/media/livestreams", new SaveLivestreamRequest(
            "Sunday service", start, "https://www.youtube.com/live/y0jPz7KFw_o", "# Behold my servant\n- He will not cry out", "https://pay.yoco.com/shapers-church", null)))
            .ReadAsync<LivestreamAdminDto>();

        var upcoming = await (await anonymous.GetAsync("/api/media/live")).ReadAsync<LiveNowDto>();
        Assert.Equal(LiveState.Upcoming, upcoming.State);
        Assert.Equal(stream.Id, upcoming.Stream!.Id);

        stream = await (await admin.PostJsonAsync($"/api/admin/media/livestreams/{stream.Id}/cues", new AddCueRequest("Isa 42:1-4", "Behold, my servant, whom I uphold"))).ReadAsync<LivestreamAdminDto>();
        await (await admin.PostAsync($"/api/admin/media/livestreams/{stream.Id}/go-live", null)).ReadAsync<LivestreamAdminDto>();
        await (await admin.PostJsonAsync($"/api/admin/media/livestreams/{stream.Id}/on-screen", new ShowCueRequest(stream.Cues[0].Id))).ReadAsync<LivestreamAdminDto>();

        var response = await anonymous.GetAsync("/api/media/live");
        Assert.Equal("public, max-age=10", response.Headers.CacheControl?.ToString());
        var live = await response.ReadAsync<LiveNowDto>();
        Assert.Equal(LiveState.Live, live.State);
        Assert.Equal("y0jPz7KFw_o", live.Stream!.YouTubeId);
        Assert.Equal("Isaiah 42:1–4", live.Stream.OnScreen!.Reference);
        Assert.Equal("https://pay.yoco.com/shapers-church", live.Stream.GiveUrl);

        await (await admin.PostAsync($"/api/admin/media/livestreams/{stream.Id}/end", null)).ReadAsync<LivestreamAdminDto>();
        var afterwards = await (await anonymous.GetAsync("/api/media/live")).ReadAsync<LiveNowDto>();
        Assert.NotEqual(LiveState.Live, afterwards.State);

        var sermonId = await (await admin.PostAsync($"/api/admin/media/livestreams/{stream.Id}/make-sermon", null)).ReadAsync<Guid>();
        var sermon = await (await admin.GetAsync($"/api/admin/media/sermons/{sermonId}")).ReadAsync<SermonAdminDto>();
        Assert.Equal(SermonStatus.Draft, sermon.Status);
        Assert.Equal("y0jPz7KFw_o", sermon.Sermon.Video!.ExternalId);
        Assert.Equal(["Isaiah 42:1–4"], sermon.Sermon.Scripture.Select(s => s.Display));
        Assert.Contains("He will not cry out", sermon.Sermon.Notes, StringComparison.Ordinal);

        var again = await admin.PostAsync($"/api/admin/media/livestreams/{stream.Id}/make-sermon", null);
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
    }

    [Fact]
    public async Task Times_sent_with_a_south_african_offset_are_stored_as_the_same_instant()
    {
        var admin = await api.SignInAdminAsync();
        var sunday = new DateTimeOffset(2026, 10, 11, 9, 0, 0, TimeSpan.FromHours(2));

        var stream = await (await admin.PostJsonAsync("/api/admin/media/livestreams", new SaveLivestreamRequest("Local time", sunday, null, null, null, null)))
            .ReadAsync<LivestreamAdminDto>();

        var stored = await (await admin.GetAsync($"/api/admin/media/livestreams/{stream.Id}")).ReadAsync<LivestreamAdminDto>();
        Assert.Equal(sunday, stored.ScheduledStart);
        Assert.Equal(TimeSpan.Zero, stored.ScheduledStart.Offset);
    }

    [Fact]
    public async Task Going_live_without_a_video_link_is_refused()
    {
        var admin = await api.SignInAdminAsync();
        var stream = await (await admin.PostJsonAsync("/api/admin/media/livestreams", new SaveLivestreamRequest("No link yet", DateTimeOffset.UtcNow.AddDays(2), null, null, null, null)))
            .ReadAsync<LivestreamAdminDto>();

        var response = await admin.PostAsync($"/api/admin/media/livestreams/{stream.Id}/go-live", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("YouTube Live link", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_guest_connect_card_creates_a_record_with_consent_and_reaches_staff()
    {
        var guest = api.Browser();
        var noConsent = await guest.PostJsonAsync("/api/connect", Card("Visitor", "One", "082 555 0301", consent: false));
        Assert.Equal(HttpStatusCode.BadRequest, noConsent.StatusCode);

        var receipt = await (await guest.PostJsonAsync("/api/connect", Card("Visitor", "Two", "082 555 0302", consent: true))).ReadAsync<ConnectCardReceipt>();

        var admin = await api.SignInAdminAsync();
        var cards = await (await admin.GetAsync("/api/admin/connect-cards?status=New")).ReadAsync<List<ConnectCardDto>>();
        var card = Assert.Single(cards, c => c.Id == receipt.Id);
        Assert.Equal("Visitor Two", card.PersonName);
        Assert.Equal("+27825550302", card.Mobile);
        Assert.Contains(ConnectReason.FirstTime, card.Reasons);
        Assert.True(card.IsNewPerson);

        var person = await (await admin.GetAsync($"/api/admin/people/{card.PersonId}")).ReadAsync<PersonDetailDto>();
        Assert.Equal(PersonSource.VisitorCard, person.Source);
        Assert.Contains(person.Consents, c => c.Purpose == ConsentPurposes.ChurchRecord && c.Granted);

        (await admin.PostJsonAsync($"/api/admin/connect-cards/{card.Id}/handled", new HandleConnectCardRequest("Called on Monday"))).EnsureSuccessStatusCode();
        var handled = await (await admin.GetAsync("/api/admin/connect-cards?status=Handled")).ReadAsync<List<ConnectCardDto>>();
        Assert.Contains(handled, c => c.Id == card.Id && c.HandlerNote == "Called on Monday");
    }

    [Fact]
    public async Task A_signed_in_member_card_goes_on_their_own_record()
    {
        var member = api.Browser();
        var request = await (await member.PostJsonAsync("/api/auth/otp/request", new { phone = "082 555 0303" })).ReadAsync<RequestCodeResponse>();
        var verified = await (await member.PostJsonAsync("/api/auth/otp/verify", new { request.ChallengeId, code = api.Sms.LastCodeFor("+27825550303") })).ReadAsync<SignInResponse>();
        var registered = await (await member.PostJsonAsync("/api/auth/register", new
        {
            request.ChallengeId,
            registrationTicket = verified.RegistrationTicket,
            firstName = "Faithful",
            lastName = "Member",
            policyVersion = "2026-09",
            consents = new[] { new { purpose = "processing.church_record", granted = true } },
        })).ReadAsync<SignInResponse>();
        member.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registered.Tokens!.AccessToken);
        var profile = await (await member.GetAsync("/api/me/profile")).ReadAsync<PersonDetailDto>();

        var receipt = await (await member.PostJsonAsync("/api/connect", new ConnectCardRequest(null, null, null, null, [ConnectReason.Prayer], "Please pray for my mother", "livestream", null, false, null)))
            .ReadAsync<ConnectCardReceipt>();

        var admin = await api.SignInAdminAsync();
        var cards = await (await admin.GetAsync("/api/admin/connect-cards")).ReadAsync<List<ConnectCardDto>>();
        var card = Assert.Single(cards, c => c.Id == receipt.Id);
        Assert.Equal(profile.Id, card.PersonId);
        Assert.False(card.IsNewPerson);
    }

    private static ConnectCardRequest Card(string first, string last, string mobile, bool consent) =>
        new(first, last, mobile, null, [ConnectReason.FirstTime], "Loved the service", "livestream", null, consent, "2026-09");
}
