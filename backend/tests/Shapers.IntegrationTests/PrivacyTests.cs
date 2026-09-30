using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shapers.Communications.Application;
using Shapers.Identity.Application;
using Shapers.People.Application;
using Shapers.People.Contracts;
using Shapers.People.Domain;
using Shapers.Platform.Persistence;
using Shapers.Prayer.Application;
using Shapers.Prayer.Domain;
using Shapers.Prayer.Infrastructure;
using Shapers.Privacy.Application;
using Shapers.Privacy.Domain;

namespace Shapers.IntegrationTests;

public sealed class PrivacyTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task A_member_downloads_everything_held_about_them()
    {
        var member = await SignInMemberAsync("082 555 5001", "+27825555001", "Zanele", "Mthembu");
        (await member.PostJsonAsync("/api/prayer/requests", new SubmitPrayerRequest("Pray for my exams.", false, false, true))).EnsureSuccessStatusCode();
        (await member.PostJsonAsync("/api/me/devices", new RegisterDeviceRequest("ExponentPushToken[zanele]", "android", "Zanele's phone"))).EnsureSuccessStatusCode();

        var response = await member.GetAsync("/api/me/data-export");

        response.EnsureSuccessStatusCode();
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = json.RootElement.GetProperty("data");
        Assert.Equal("Zanele", data.GetProperty("Church record").GetProperty("profile").GetProperty("firstName").GetString());
        Assert.Equal("+27825555001", data.GetProperty("Login").GetProperty("phoneNumber").GetString());
        Assert.Contains("Pray for my exams.", data.GetProperty("Prayer requests").GetRawText(), StringComparison.Ordinal);
        Assert.Contains("Zanele's phone", data.GetProperty("Notifications").GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_approved_deletion_request_erases_the_person_everywhere()
    {
        var admin = await api.SignInAdminAsync();
        var member = await SignInMemberAsync("082 555 5002", "+27825555002", "Thabo", "Sithole");
        var profile = await (await member.GetAsync("/api/me/profile")).ReadAsync<PersonDetailDto>();
        (await member.PostJsonAsync("/api/prayer/requests", new SubmitPrayerRequest("Pray for my health.", false, false, true))).EnsureSuccessStatusCode();
        (await member.PostJsonAsync("/api/me/devices", new RegisterDeviceRequest("ExponentPushToken[thabo]", "android", null))).EnsureSuccessStatusCode();

        var request = await (await member.PostJsonAsync("/api/me/privacy-requests", new SubmitDataRequest(DataRequestType.Deletion, "I'm moving overseas."))).ReadAsync<MyDataRequestDto>();
        Assert.Equal(HttpStatusCode.Conflict, (await member.PostJsonAsync("/api/me/privacy-requests", new SubmitDataRequest(DataRequestType.Deletion, null))).StatusCode);

        var queue = await (await admin.GetAsync("/api/admin/privacy/requests")).ReadAsync<List<DataRequestAdminDto>>();
        Assert.Contains(queue, q => q.Id == request.Id && q.PersonName == "Thabo Sithole" && !q.Overdue);
        (await admin.PostJsonAsync($"/api/admin/privacy/requests/{request.Id}/complete", new DecideDataRequest("Your details have been deleted."))).EnsureSuccessStatusCode();

        // The church record is an empty shell, hidden from the people list.
        var erased = await (await admin.GetAsync($"/api/admin/people/{profile.Id}")).ReadAsync<PersonDetailDto>();
        Assert.Equal("Removed", erased.FirstName);
        Assert.Empty(erased.Contacts);
        var prayers = await (await admin.GetAsync("/api/admin/prayer")).ReadAsync<List<PrayerAdminDto>>();
        Assert.DoesNotContain(prayers, p => p.PersonId == profile.Id);

        // The login is gone: the same number starts afresh.
        var fresh = api.Browser();
        var challenge = await (await fresh.PostJsonAsync("/api/auth/otp/request", new { phone = "082 555 5002" })).ReadAsync<RequestCodeResponse>();
        var verified = await (await fresh.PostJsonAsync("/api/auth/otp/verify", new { challenge.ChallengeId, code = api.Sms.LastCodeFor("+27825555002") })).ReadAsync<SignInResponse>();
        Assert.Equal(SignInStatus.RegistrationRequired, verified.Status);

        await using var scope = api.Services.CreateAsyncScope();
        var communications = scope.ServiceProvider.GetRequiredService<ICommunicationsDb>();
        Assert.False(await communications.Devices.AnyAsync(d => d.PersonId == profile.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Declining_a_request_tells_the_person_why()
    {
        var admin = await api.SignInAdminAsync();
        var member = await SignInMemberAsync("082 555 5003", "+27825555003", "Ayanda", "Khoza");
        Assert.Equal(HttpStatusCode.BadRequest, (await member.PostJsonAsync("/api/me/privacy-requests", new SubmitDataRequest(DataRequestType.Correction, null))).StatusCode);
        var request = await (await member.PostJsonAsync("/api/me/privacy-requests", new SubmitDataRequest(DataRequestType.Correction, "My birthday is wrong."))).ReadAsync<MyDataRequestDto>();

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostJsonAsync($"/api/admin/privacy/requests/{request.Id}/decline", new DecideDataRequest(null))).StatusCode);
        (await admin.PostJsonAsync($"/api/admin/privacy/requests/{request.Id}/decline", new DecideDataRequest("Please send us your ID so we can check."))).EnsureSuccessStatusCode();

        var mine = await (await member.GetAsync("/api/me/privacy-requests")).ReadAsync<List<MyDataRequestDto>>();
        var decided = Assert.Single(mine);
        Assert.Equal(DataRequestStatus.Declined, decided.Status);
        Assert.Equal("Please send us your ID so we can check.", decided.Response);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/admin/privacy/requests")).StatusCode);
    }

    [Fact]
    public async Task Retention_removes_old_prayer_requests_and_forgotten_guests_but_never_recent_audit_entries()
    {
        var guest = api.Browser();
        (await guest.PostJsonAsync("/api/connect", new ConnectCardRequest("Old", "Visitor", null, "old.visitor@example.com", [ConnectReason.FirstTime], null, "website", null, true, "2026-09")))
            .EnsureSuccessStatusCode();

        await using var scope = api.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var ct = TestContext.Current.CancellationToken;
        var prayerDb = services.GetRequiredService<PrayerDbContext>();
        var old = PrayerRequest.Submit(Guid.NewGuid(), ScopePath.Parse("shapers"), "An old request", PrayerVisibility.PastorsOnly, false, PrayerSource.App, DateTimeOffset.UtcNow.AddDays(-400));
        prayerDb.Requests.Add(old);
        await prayerDb.SaveChangesAsync(ct);
        await prayerDb.Database.ExecuteSqlRawAsync("UPDATE people.persons SET created_at = now() - interval '100 days' WHERE first_name = 'Old' AND last_name = 'Visitor'", ct);

        Assert.True(await services.GetRequiredService<PrayerRetentionJob>().RunAsync(ct) >= 1);
        Assert.False(await prayerDb.Requests.AnyAsync(r => r.Id == old.Id, ct));
        Assert.True(await services.GetRequiredService<GuestRetentionJob>().RunAsync(ct) >= 1);
        Assert.True(await prayerDb.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM people.persons WHERE first_name = 'Old' AND last_name = 'Visitor'").SingleAsync(ct) == 0);

        // The audit log is evidence: recent entries still can't be deleted, whoever asks.
        var platform = services.GetRequiredService<PlatformDbContext>();
        await Assert.ThrowsAnyAsync<Exception>(() => platform.Database.ExecuteSqlRawAsync("DELETE FROM platform.audit_entries", ct));
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
