using System.Net;
using System.Net.Http.Headers;
using Shapers.Events.Application;
using Shapers.Events.Domain;
using Shapers.Identity.Application;
using Shapers.People.Application;
using Shapers.People.Contracts;
using Shapers.People.Domain;

namespace Shapers.IntegrationTests;

public sealed class EventTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static readonly ConsentDecision[] Consents = [new(ConsentPurposes.ChurchRecord, true)];

    [Fact]
    public async Task A_member_books_their_household_and_the_waitlist_moves_up_when_they_cancel()
    {
        var admin = await api.SignInAdminAsync();
        var e = await CreatePublishedAsync(admin, "Family fun day", capacity: 2, waitlist: true);

        var (parent, parentId) = await SignInMemberAsync("082 555 1001", "+27825551001", "Thandi", "Mokoena");
        var child = await (await admin.PostJsonAsync("/api/admin/people", new CreatePersonRequest("Kea", "Mokoena", null, new DateOnly(2018, 3, 1), null, null, null, null, null)))
            .ReadAsync<PersonDetailDto>();
        (await admin.PostJsonAsync("/api/admin/households", new CreateHouseholdRequest("Mokoena", null,
            [new HouseholdMemberRequest(parentId, HouseholdRole.Adult), new HouseholdMemberRequest(child.Id, HouseholdRole.Child)]))).EnsureSuccessStatusCode();

        var household = await (await parent.GetAsync("/api/me/household")).ReadAsync<List<HouseholdMemberSummary>>();
        Assert.Contains(household, m => m.PersonId == child.Id && m.IsChild);

        var booked = await (await parent.PostJsonAsync($"/api/events/{e.Event.Slug}/register", new MemberRegisterRequest([parentId, child.Id], null)))
            .ReadAsync<RegistrationDto>();
        Assert.Equal(RegistrationStatus.Confirmed, booked.Status);
        Assert.Equal(2, booked.Tickets.Count);

        // Registering again, or registering someone outside the household, is refused.
        Assert.Equal(HttpStatusCode.Conflict, (await parent.PostJsonAsync($"/api/events/{e.Event.Slug}/register", new MemberRegisterRequest([parentId], null))).StatusCode);
        var stranger = await (await admin.PostJsonAsync("/api/admin/people", new CreatePersonRequest("Not", "Family", null, null, null, null, null, null, null))).ReadAsync<PersonDetailDto>();
        Assert.Equal(HttpStatusCode.Forbidden, (await parent.PostJsonAsync($"/api/events/{e.Event.Slug}/register", new MemberRegisterRequest([stranger.Id], null))).StatusCode);

        var (second, secondId) = await SignInMemberAsync("082 555 1002", "+27825551002", "Bongani", "Zulu");
        var waiting = await (await second.PostJsonAsync($"/api/events/{e.Event.Slug}/register", new MemberRegisterRequest([secondId], null))).ReadAsync<RegistrationDto>();
        Assert.Equal(RegistrationStatus.Waitlisted, waiting.Status);
        Assert.Equal(1, waiting.WaitlistPosition);
        Assert.All(waiting.Tickets, t => Assert.Empty(t.Code));

        (await parent.PostAsync($"/api/me/registrations/{booked.Id}/cancel", null)).EnsureSuccessStatusCode();

        var mine = await (await second.GetAsync("/api/me/registrations")).ReadAsync<List<RegistrationDto>>();
        var promoted = Assert.Single(mine);
        Assert.Equal(RegistrationStatus.Confirmed, promoted.Status);
        var ticket = Assert.Single(promoted.Tickets);

        var first = await (await admin.PostJsonAsync($"/api/admin/events/{e.Event.Id}/check-in", new CheckInRequest($"SHAPERS-T:{ticket.Code}", null))).ReadAsync<CheckInResultDto>();
        Assert.Equal(CheckInOutcome.CheckedIn, first.Outcome);
        var again = await (await admin.PostJsonAsync($"/api/admin/events/{e.Event.Id}/check-in", new CheckInRequest(ticket.Code.ToLowerInvariant(), null))).ReadAsync<CheckInResultDto>();
        Assert.Equal(CheckInOutcome.AlreadyCheckedIn, again.Outcome);

        // A checked-in booking can't be cancelled.
        Assert.Equal(HttpStatusCode.BadRequest, (await second.PostAsync($"/api/me/registrations/{promoted.Id}/cancel", null)).StatusCode);
    }

    [Fact]
    public async Task A_guest_proves_their_email_registers_and_manages_the_booking_with_their_key()
    {
        var admin = await api.SignInAdminAsync();
        var e = await CreatePublishedAsync(admin, "Newcomers lunch", capacity: 20, waitlist: false);
        var guest = api.Browser();
        const string email = "guest.one@example.com";

        var code = await (await guest.PostJsonAsync($"/api/events/{e.Event.Slug}/guest-code", new GuestCodeRequest(email))).ReadAsync<GuestCodeResponse>();
        var wrong = await guest.PostJsonAsync($"/api/events/{e.Event.Slug}/register-guest", Guest(code.VerificationId, "000000", email));
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);

        var receipt = await (await guest.PostJsonAsync($"/api/events/{e.Event.Slug}/register-guest", Guest(code.VerificationId, api.Email.LastCodeFor(email), email, "Palesa Molefe")))
            .ReadAsync<GuestRegistrationReceipt>();
        Assert.Equal(RegistrationStatus.Confirmed, receipt.Registration.Status);
        Assert.Equal(["Ayanda Guest", "Palesa Molefe"], receipt.Registration.Tickets.Select(t => t.Name).Order());
        Assert.Contains(api.Email.Sent, m => m.To == email && m.Subject.StartsWith("You're booked", StringComparison.Ordinal) && m.PlainText.Contains($"key={Uri.EscapeDataString(receipt.AccessKey)}", StringComparison.Ordinal));

        // A code works once.
        var reused = await guest.PostJsonAsync($"/api/events/{e.Event.Slug}/register-guest", Guest(code.VerificationId, api.Email.LastCodeFor(email), email));
        Assert.Equal(HttpStatusCode.Unauthorized, reused.StatusCode);

        var url = $"/api/events/registrations/{receipt.Registration.Id}";
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"{url}?key=not-the-key")).StatusCode);
        var viewed = await (await guest.GetAsync($"{url}?key={Uri.EscapeDataString(receipt.AccessKey)}")).ReadAsync<RegistrationDto>();
        Assert.Equal(receipt.Registration.Id, viewed.Id);

        (await guest.PostAsync($"{url}/cancel?key={Uri.EscapeDataString(receipt.AccessKey)}", null)).EnsureSuccessStatusCode();
        var cancelled = await (await guest.GetAsync($"{url}?key={Uri.EscapeDataString(receipt.AccessKey)}")).ReadAsync<RegistrationDto>();
        Assert.Equal(RegistrationStatus.Cancelled, cancelled.Status);
    }

    [Fact]
    public async Task Simultaneous_bookings_never_oversell_the_last_seats()
    {
        var admin = await api.SignInAdminAsync();
        var e = await CreatePublishedAsync(admin, "Marriage workshop", capacity: 3, waitlist: false);

        var responses = await Task.WhenAll(Enumerable.Range(1, 8).Select(i =>
            admin.PostJsonAsync($"/api/admin/events/{e.Event.Id}/registrations", new AdminRegisterRequest(null, "Walk", $"In {i}", null, $"walk.in.{i}@example.com", true, null, null))));

        var bodies = await Task.WhenAll(responses.Select(async r => $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}"));
        Assert.True(responses.Count(r => r.IsSuccessStatusCode) == 3, string.Join(Environment.NewLine, bodies));
        Assert.All(responses.Where(r => !r.IsSuccessStatusCode), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        var stored = await (await admin.GetAsync($"/api/admin/events/{e.Event.Id}")).ReadAsync<EventAdminDto>();
        Assert.Equal(3, stored.Confirmed);
        Assert.Equal(0, stored.Event.SeatsLeft);
    }

    [Fact]
    public async Task Door_volunteers_check_people_in_but_cannot_see_attendee_details()
    {
        var admin = await api.SignInAdminAsync();
        var e = await CreatePublishedAsync(admin, "Youth night", capacity: null, waitlist: false);
        var booking = await (await admin.PostJsonAsync($"/api/admin/events/{e.Event.Id}/registrations", new AdminRegisterRequest(null, "=HYPERLINK(1)", "Smith", null, "smith@example.com", true, null, null)))
            .ReadAsync<RegistrationDto>();

        var door = await SignInStaffWithRoleAsync(admin, "door@test.local", "Door volunteer");
        Assert.Equal(HttpStatusCode.Forbidden, (await door.GetAsync($"/api/admin/events/{e.Event.Id}/attendees")).StatusCode);
        var list = await (await door.GetAsync($"/api/admin/events/{e.Event.Id}/door-list?q=smith")).ReadAsync<List<TicketDto>>();
        var row = Assert.Single(list);
        var result = await (await door.PostJsonAsync($"/api/admin/events/{e.Event.Id}/check-in", new CheckInRequest(null, row.AttendeeId))).ReadAsync<CheckInResultDto>();
        Assert.Equal(CheckInOutcome.CheckedIn, result.Outcome);

        // The export neutralises spreadsheet formulas.
        var csv = await (await admin.GetAsync($"/api/admin/events/{e.Event.Id}/attendees.csv")).Content.ReadAsStringAsync();
        Assert.Contains("'=HYPERLINK(1) Smith", csv, StringComparison.Ordinal);
        Assert.Contains("smith@example.com", csv, StringComparison.Ordinal);
        Assert.Equal(booking.Id, (await (await admin.GetAsync($"/api/admin/events/{e.Event.Id}/attendees")).ReadAsync<List<AttendeeRowDto>>()).Single().RegistrationId);
    }

    [Fact]
    public async Task Members_only_and_draft_events_are_hidden_from_the_public()
    {
        var admin = await api.SignInAdminAsync();
        var draft = await (await admin.PostJsonAsync("/api/admin/events", Save("Planning meeting", null, false))).ReadAsync<EventAdminDto>();
        var members = await (await admin.PostJsonAsync("/api/admin/events", Save("Members meeting", null, false) with { Visibility = EventVisibility.Members })).ReadAsync<EventAdminDto>();
        (await admin.PostAsync($"/api/admin/events/{members.Event.Id}/publish", null)).EnsureSuccessStatusCode();

        var anonymous = api.Browser();
        var listed = await (await anonymous.GetAsync("/api/events")).ReadAsync<List<EventDto>>();
        Assert.DoesNotContain(listed, x => x.Id == draft.Event.Id || x.Id == members.Event.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/events/{members.Event.Slug}")).StatusCode);

        var (member, _) = await SignInMemberAsync("082 555 1003", "+27825551003", "Musa", "Ndlovu");
        var seen = await (await member.GetAsync("/api/events")).ReadAsync<List<EventDto>>();
        Assert.Contains(seen, x => x.Id == members.Event.Id);
        Assert.DoesNotContain(seen, x => x.Id == draft.Event.Id);
    }

    private static SaveEventRequest Save(string title, int? capacity, bool waitlist) => new(
        title, "A summary", null, DateTimeOffset.UtcNow.AddDays(10), DateTimeOffset.UtcNow.AddDays(10).AddHours(3),
        new LocationDto("Shapers Church", "8 Mellis Road, Rivonia"), null, EventVisibility.Public, true, null, null, capacity, waitlist, 4, [], null);

    private static async Task<EventAdminDto> CreatePublishedAsync(HttpClient admin, string title, int? capacity, bool waitlist)
    {
        var e = await (await admin.PostJsonAsync("/api/admin/events", Save(title, capacity, waitlist))).ReadAsync<EventAdminDto>();
        return await (await admin.PostAsync($"/api/admin/events/{e.Event.Id}/publish", null)).ReadAsync<EventAdminDto>();
    }

    private static GuestRegisterRequest Guest(Guid verificationId, string code, string email, params string[] others) =>
        new(verificationId, code, "Ayanda", "Guest", email, null, others, null, true, "2026-09");

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

    private async Task<HttpClient> SignInStaffWithRoleAsync(HttpClient admin, string email, string roleName)
    {
        var person = await (await admin.PostJsonAsync("/api/admin/people", new CreatePersonRequest("Door", "Volunteer", null, null, null, null, null, null, null))).ReadAsync<PersonDetailDto>();
        var setup = await (await admin.PostJsonAsync($"/api/admin/people/{person.Id}/staff-login", new { email })).ReadAsync<StaffLoginSetupDto>();
        const string password = "a-long-enough-password";
        (await admin.PostJsonAsync("/api/auth/staff/set-password", new { setup.UserId, token = setup.SetupToken, password })).EnsureSuccessStatusCode();
        var roles = await (await admin.GetAsync("/api/admin/roles")).ReadAsync<List<RoleDto>>();
        var role = roles.Single(r => r.Name == roleName);
        await (await admin.PostJsonAsync("/api/admin/grants", new CreateGrantRequest(person.Id, role.Id, "shapers", null, null))).ReadAsync<GrantDto>();
        return await api.SignInStaffAsync(email, password);
    }
}
