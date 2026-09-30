using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Shapers.Communications.Application;
using Shapers.Communications.Domain;
using Shapers.Identity.Application;
using Shapers.People.Application;
using Shapers.People.Contracts;
using Shapers.People.Domain;

namespace Shapers.IntegrationTests;

public sealed class AnnouncementTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task A_church_wide_announcement_needs_a_second_approver_then_reaches_the_app_and_email()
    {
        var admin = await api.SignInAdminAsync();
        var pastor = await SignInStaffWithRoleAsync(admin, "pastor@test.local", "Campus pastor");

        // Someone without the app who asked for church emails, and a member with the app.
        var reader = await (await admin.PostJsonAsync("/api/admin/people", new CreatePersonRequest("Email", "Reader", null, null, null, null, null, "reader@example.com", null)))
            .ReadAsync<PersonDetailDto>();
        (await admin.PostJsonAsync($"/api/admin/people/{reader.Id}/consents", new RecordConsentRequest(
            [new ConsentDecisionDto(ConsentPurposes.EmailCommunication, true)], "2026-09", ConsentSource.PaperForm))).EnsureSuccessStatusCode();
        var member = await SignInMemberAsync("082 555 4001", "+27825554001", "Neo", "Molefe");
        await AllowPushAsync(member, "ExponentPushToken[neo-phone]");

        var draft = await (await admin.PostJsonAsync("/api/admin/announcements", new SaveAnnouncementRequest(
            "Family fun day", "Join us this Saturday at 10:00.\nBring the whole family!", "/events", "shapers", true, null))).ReadAsync<AnnouncementDto>();
        Assert.True(draft.NeedsApproval);
        Assert.True(draft.Audience!.ByEmail >= 1);
        Assert.True(draft.Audience.WithApp >= 1);

        var submitted = await (await admin.PostAsync($"/api/admin/announcements/{draft.Id}/submit", null)).ReadAsync<AnnouncementDto>();
        Assert.Equal(AnnouncementStatus.AwaitingApproval, submitted.Status);

        // The author can't approve their own announcement; a second person can.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync($"/api/admin/announcements/{draft.Id}/approve", null)).StatusCode);
        var approved = await (await pastor.PostAsync($"/api/admin/announcements/{draft.Id}/approve", null)).ReadAsync<AnnouncementDto>();
        Assert.Equal(AnnouncementStatus.Queued, approved.Status);

        await RunAsync<AnnouncementDispatchJob>(s => s.RunAsync(TestContext.Current.CancellationToken));
        await RunAsync<DeliveryJob>(s => s.RunAsync(TestContext.Current.CancellationToken));

        var inbox = await (await member.GetAsync("/api/me/notifications")).ReadAsync<InboxDto>();
        Assert.Contains(inbox.Items, n => n.Title == "Family fun day" && n.Link == "/events");
        Assert.Contains(api.Push.Sent, p => p.Token == "ExponentPushToken[neo-phone]" && p.Title == "Family fun day");

        var email = Assert.Single(api.Email.Sent, m => m.To == "reader@example.com" && m.Subject == "Family fun day");
        Assert.Contains("Bring the whole family!", email.PlainText, StringComparison.Ordinal);
        var sent = await (await admin.GetAsync($"/api/admin/announcements/{draft.Id}")).ReadAsync<AnnouncementDto>();
        Assert.Equal(AnnouncementStatus.Sent, sent.Status);
        Assert.True(sent.Delivered!.Emailed >= 1);
        Assert.True(sent.Delivered.Pushed >= 1);

        // The unsubscribe link works once, can't be forged, and stops the next email.
        var link = email.PlainText.Split("Unsubscribe: ")[1].Trim();
        Assert.StartsWith("https://api.test/api/unsubscribe?", link, StringComparison.Ordinal);
        var path = link["https://api.test".Length..];
        var anonymous = api.Browser();
        Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.GetAsync(path[..^4] + "0000")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(path)).StatusCode);

        var second = await (await admin.PostJsonAsync("/api/admin/announcements", new SaveAnnouncementRequest(
            "Carols by candlelight", "Save the date.", null, "shapers", true, null))).ReadAsync<AnnouncementDto>();
        (await admin.PostAsync($"/api/admin/announcements/{second.Id}/submit", null)).EnsureSuccessStatusCode();
        (await pastor.PostAsync($"/api/admin/announcements/{second.Id}/approve", null)).EnsureSuccessStatusCode();
        await RunAsync<AnnouncementDispatchJob>(s => s.RunAsync(TestContext.Current.CancellationToken));
        await RunAsync<DeliveryJob>(s => s.RunAsync(TestContext.Current.CancellationToken));
        Assert.DoesNotContain(api.Email.Sent, m => m.To == "reader@example.com" && m.Subject == "Carols by candlelight");
    }

    [Fact]
    public async Task Approvers_can_send_an_announcement_back_with_a_note()
    {
        var admin = await api.SignInAdminAsync();
        var pastor = await SignInStaffWithRoleAsync(admin, "pastor2@test.local", "Campus pastor");
        var draft = await (await admin.PostJsonAsync("/api/admin/announcements", new SaveAnnouncementRequest("Picnic", "Sunday after the service.", null, "shapers", false, null)))
            .ReadAsync<AnnouncementDto>();
        (await admin.PostAsync($"/api/admin/announcements/{draft.Id}/submit", null)).EnsureSuccessStatusCode();

        var returned = await (await pastor.PostJsonAsync($"/api/admin/announcements/{draft.Id}/return", new ReturnAnnouncementRequest("Which Sunday?"))).ReadAsync<AnnouncementDto>();

        Assert.Equal(AnnouncementStatus.Draft, returned.Status);
        Assert.Equal("Which Sunday?", returned.ReturnNote);
    }

    [Fact]
    public async Task Members_cannot_send_announcements()
    {
        var member = await SignInMemberAsync("082 555 4002", "+27825554002", "Lwazi", "Ndlovu");
        var response = await member.PostJsonAsync("/api/admin/announcements", new SaveAnnouncementRequest("Hi", "Everyone", null, "shapers", false, null));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task RunAsync<TJob>(Func<TJob, Task> run)
        where TJob : notnull
    {
        await using var scope = api.Services.CreateAsyncScope();
        await run(scope.ServiceProvider.GetRequiredService<TJob>());
    }

    private static async Task AllowPushAsync(HttpClient member, string token)
    {
        (await member.PostJsonAsync("/api/me/consents", new RecordConsentRequest([new ConsentDecisionDto(ConsentPurposes.PushNotifications, true)], "2026-09", ConsentSource.MobileApp)))
            .EnsureSuccessStatusCode();
        (await member.PostJsonAsync("/api/me/devices", new RegisterDeviceRequest(token, "android", "Test phone"))).EnsureSuccessStatusCode();
    }

    private async Task<HttpClient> SignInStaffWithRoleAsync(HttpClient admin, string email, string roleName)
    {
        var person = await (await admin.PostJsonAsync("/api/admin/people", new CreatePersonRequest("Staff", roleName, null, null, null, null, null, null, null))).ReadAsync<PersonDetailDto>();
        var setup = await (await admin.PostJsonAsync($"/api/admin/people/{person.Id}/staff-login", new { email })).ReadAsync<StaffLoginSetupDto>();
        const string password = "a-long-enough-password";
        (await admin.PostJsonAsync("/api/auth/staff/set-password", new { setup.UserId, token = setup.SetupToken, password })).EnsureSuccessStatusCode();
        var roles = await (await admin.GetAsync("/api/admin/roles")).ReadAsync<List<RoleDto>>();
        var role = roles.Single(r => r.Name == roleName);
        await (await admin.PostJsonAsync("/api/admin/grants", new CreateGrantRequest(person.Id, role.Id, "shapers", null, null))).ReadAsync<GrantDto>();
        return await api.SignInStaffAsync(email, password);
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
