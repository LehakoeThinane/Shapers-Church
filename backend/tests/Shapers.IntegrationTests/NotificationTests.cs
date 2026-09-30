using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Shapers.Communications.Application;
using Shapers.Communications.Domain;
using Shapers.Identity.Application;
using Shapers.Media.Application;
using Shapers.People.Application;
using Shapers.People.Contracts;
using Shapers.People.Domain;
using Shapers.Prayer.Application;

namespace Shapers.IntegrationTests;

public sealed class NotificationTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Going_live_reaches_the_inbox_and_pushes_only_to_members_who_allow_it()
    {
        var admin = await api.SignInAdminAsync();
        var keen = await SignInMemberAsync("082 555 3001", "+27825553001", "Lebo", "Mokoena");
        await AllowPushAsync(keen, "ExponentPushToken[keen-phone]");
        var quiet = await SignInMemberAsync("082 555 3002", "+27825553002", "Tumi", "Nkosi");
        await AllowPushAsync(quiet, "ExponentPushToken[quiet-phone]");
        (await quiet.PutAsJsonAsync("/api/me/notification-preferences", new SetPreferenceRequest(Topic.Live, Channel.Push, false), ApiFactory.Json)).EnsureSuccessStatusCode();
        var noConsent = await SignInMemberAsync("082 555 3003", "+27825553003", "Sizwe", "Dlamini");
        (await noConsent.PostJsonAsync("/api/me/devices", new RegisterDeviceRequest("ExponentPushToken[no-consent-phone]", "android", null))).EnsureSuccessStatusCode();

        var stream = await (await admin.PostJsonAsync("/api/admin/media/livestreams", new SaveLivestreamRequest(
            "Sunday service", DateTimeOffset.UtcNow.AddMinutes(5), "https://www.youtube.com/live/y0jPz7KFw_o", null, null, null))).ReadAsync<LivestreamAdminDto>();
        (await admin.PostAsync($"/api/admin/media/livestreams/{stream.Id}/go-live", null)).EnsureSuccessStatusCode();

        // Everyone with the app gets it in their inbox, whatever their push settings.
        foreach (var member in new[] { keen, quiet, noConsent })
        {
            var item = await WaitForAsync(member, n => n.Link == "/live");
            Assert.Equal("We're live", item.Title);
            Assert.False(item.Read);
        }

        await RunDeliveryAsync();

        var pushes = api.Push.Sent.Where(p => p.Link == "/live").Select(p => p.Token).ToList();
        Assert.Contains("ExponentPushToken[keen-phone]", pushes);
        Assert.DoesNotContain("ExponentPushToken[quiet-phone]", pushes);
        Assert.DoesNotContain("ExponentPushToken[no-consent-phone]", pushes);

        // Reading it clears the unread count.
        var inbox = await (await keen.GetAsync("/api/me/notifications")).ReadAsync<InboxDto>();
        (await keen.PostAsync("/api/me/notifications/read-all", null)).EnsureSuccessStatusCode();
        Assert.True(inbox.Unread > 0);
        Assert.Equal(0, (await (await keen.GetAsync("/api/me/notifications")).ReadAsync<InboxDto>()).Unread);
    }

    [Fact]
    public async Task An_approved_prayer_request_notifies_the_requester_without_its_text()
    {
        var admin = await api.SignInAdminAsync();
        var asker = await SignInMemberAsync("082 555 3004", "+27825553004", "Palesa", "Khumalo");
        var request = await (await asker.PostJsonAsync("/api/prayer/requests", new SubmitPrayerRequest("Pray for my sister's surgery.", true, false, true))).ReadAsync<MyPrayerRequestDto>();

        (await admin.PostJsonAsync($"/api/admin/prayer/{request.Id}/approve", new ReviewRequest(null, null))).EnsureSuccessStatusCode();

        var item = await WaitForAsync(asker, n => n.Link == "/prayer");
        Assert.Equal("Your request is on the prayer wall", item.Title);
        Assert.DoesNotContain("surgery", item.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Uninstalled_apps_are_forgotten_and_signing_out_stops_pushes()
    {
        var admin = await api.SignInAdminAsync();
        var member = await SignInMemberAsync("082 555 3005", "+27825553005", "Karabo", "Molefe");
        await AllowPushAsync(member, "ExponentPushToken[gone-phone]");
        await AllowPushAsync(member, "ExponentPushToken[old-phone]");
        (await member.PostJsonAsync("/api/me/devices/unregister", new UnregisterDeviceRequest("ExponentPushToken[old-phone]"))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await member.PostJsonAsync("/api/me/devices", new RegisterDeviceRequest("not-a-token", "android", null))).StatusCode);

        var stream = await (await admin.PostJsonAsync("/api/admin/media/livestreams", new SaveLivestreamRequest(
            "Midweek service", DateTimeOffset.UtcNow.AddMinutes(5), "https://www.youtube.com/live/abcdefghijk", null, null, null))).ReadAsync<LivestreamAdminDto>();
        (await admin.PostAsync($"/api/admin/media/livestreams/{stream.Id}/go-live", null)).EnsureSuccessStatusCode();
        await WaitForAsync(member, n => n.Body.Contains("Midweek service", StringComparison.Ordinal));
        await RunDeliveryAsync();

        Assert.DoesNotContain(api.Push.Sent, p => p.Token == "ExponentPushToken[old-phone]");
        var log = await (await admin.GetAsync("/api/admin/communications/deliveries")).ReadAsync<List<DeliveryLogDto>>();
        var mine = Assert.Single(log, l => l.Title == "We're live" && l.Status == DeliveryStatus.Skipped && l.Error == "The app is no longer on this phone.");
        Assert.Equal(Channel.Push, mine.Channel);
    }

    private async Task RunDeliveryAsync()
    {
        await using var scope = api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DeliveryJob>().RunAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<NotificationDto> WaitForAsync(HttpClient member, Func<NotificationDto, bool> match)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var inbox = await (await member.GetAsync("/api/me/notifications")).ReadAsync<InboxDto>();
            if (inbox.Items.FirstOrDefault(match) is { } found)
            {
                return found;
            }

            await Task.Delay(250, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException("The notification never arrived.");
    }

    private static async Task AllowPushAsync(HttpClient member, string token)
    {
        (await member.PostJsonAsync("/api/me/consents", new RecordConsentRequest([new ConsentDecisionDto(ConsentPurposes.PushNotifications, true)], "2026-09", ConsentSource.MobileApp)))
            .EnsureSuccessStatusCode();
        (await member.PostJsonAsync("/api/me/devices", new RegisterDeviceRequest(token, "android", "Test phone"))).EnsureSuccessStatusCode();
    }

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
            consents = new[] { new ConsentDecision(ConsentPurposes.ChurchRecord, true) },
        })).ReadAsync<SignInResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", signedIn.Tokens!.AccessToken);
        return client;
    }
}
