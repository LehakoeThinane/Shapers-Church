using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Shapers.Identity.Application;
using Shapers.Kids.Application;
using Shapers.People.Application;
using Shapers.People.Contracts;
using Shapers.People.Domain;

namespace Shapers.IntegrationTests;

public sealed class KidsTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task A_parent_adds_their_child_checks_in_and_the_child_is_only_handed_back_with_the_code()
    {
        var admin = await api.SignInAdminAsync();
        var (parent, _) = await SignInMemberAsync("082 555 8101", "+27825558101", "Thandi", "Mokoena");

        // The parent must confirm they're the guardian.
        var noConsent = await parent.PostJsonAsync("/api/me/kids", new AddChildRequest("Ayanda", "Mokoena", Today.AddYears(-7), null, null, null, GuardianConsent: false));
        Assert.Equal(HttpStatusCode.BadRequest, noConsent.StatusCode);

        var kids = await (await parent.PostJsonAsync("/api/me/kids", new AddChildRequest("Ayanda", "Mokoena", Today.AddYears(-7), "Peanuts: EpiPen in her bag", null, null, GuardianConsent: true)))
            .ReadAsync<List<MyChildDto>>();
        var ayanda = Assert.Single(kids);
        Assert.Equal(7, ayanda.Age);
        Assert.Equal("Primary", ayanda.ClassName);
        Assert.Equal("Peanuts: EpiPen in her bag", ayanda.CareNotes?.Allergies);

        // The child is in the parent's household now.
        var household = await (await parent.GetAsync("/api/me/household")).ReadAsync<List<HouseholdMemberSummary>>();
        Assert.Contains(household, m => m.PersonId == ayanda.PersonId && m.IsChild);

        var checkedIn = Assert.Single(await (await parent.PostJsonAsync("/api/me/kids/check-in", new ParentCheckInRequest([ayanda.PersonId]))).ReadAsync<List<MyChildDto>>());
        var code = checkedIn.Today!.PickupCode;
        Assert.Equal("Primary", checkedIn.Today.ClassName);
        Assert.Equal(HttpStatusCode.Conflict, (await parent.PostJsonAsync("/api/me/kids/check-in", new ParentCheckInRequest([ayanda.PersonId]))).StatusCode);

        // The kids team sees her in Primary with a care-notes flag, but neither the notes nor the code.
        var todayRaw = await (await admin.GetAsync("/api/admin/kids/today")).Content.ReadAsStringAsync();
        var today = System.Text.Json.JsonSerializer.Deserialize<KidsTodayDto>(todayRaw, ApiFactory.Json)!;
        var inPrimary = Assert.Single(today.Classes.Single(c => c.Name == "Primary").Children, c => c.ChildId == ayanda.PersonId);
        Assert.True(inPrimary.HasCareNotes);
        Assert.DoesNotContain(code, todayRaw, StringComparison.Ordinal);
        Assert.DoesNotContain("Peanuts", todayRaw, StringComparison.Ordinal);

        // A wrong code shows nothing; the right one shows her; handing over needs the code again.
        var wrongCode = code == "ZZZZ" ? "YYYY" : "ZZZZ";
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/admin/kids/pickup/{wrongCode}")).StatusCode);
        var pickup = await (await admin.GetAsync($"/api/admin/kids/pickup/{code.ToLowerInvariant()}")).ReadAsync<PickupDto>();
        var waiting = Assert.Single(pickup.Children);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostJsonAsync("/api/admin/kids/check-out", new CheckOutRequest(wrongCode, [waiting.CheckInId]))).StatusCode);
        (await admin.PostJsonAsync("/api/admin/kids/check-out", new CheckOutRequest(code, [waiting.CheckInId]))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/admin/kids/pickup/{code}")).StatusCode);

        var after = Assert.Single(await (await parent.GetAsync("/api/me/kids")).ReadAsync<List<MyChildDto>>());
        Assert.NotNull(after.Today!.CollectedAt);
    }

    [Fact]
    public async Task A_parent_cannot_check_in_or_change_someone_elses_child()
    {
        var (parent, _) = await SignInMemberAsync("082 555 8111", "+27825558111", "Sipho", "Dube");
        var (stranger, _) = await SignInMemberAsync("082 555 8112", "+27825558112", "Pieter", "Botha");
        var child = Assert.Single(await (await parent.PostJsonAsync("/api/me/kids", new AddChildRequest("Lwazi", "Dube", Today.AddYears(-4), null, null, null, true))).ReadAsync<List<MyChildDto>>());

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostJsonAsync("/api/me/kids/check-in", new ParentCheckInRequest([child.PersonId]))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PutAsJsonAsync($"/api/me/kids/{child.PersonId}/care-notes", new CareNotesRequest("x", null, null), ApiFactory.Json)).StatusCode);
        Assert.Empty(await (await stranger.GetAsync("/api/me/kids")).ReadAsync<List<MyChildDto>>());
    }

    [Fact]
    public async Task The_desk_checks_in_a_visiting_family_and_care_notes_need_their_own_permission()
    {
        var admin = await api.SignInAdminAsync();
        var result = await (await admin.PostJsonAsync("/api/admin/kids/desk-check-in", new DeskCheckInRequest(
            "Naledi", "Khumalo", "082 555 8121", Consent: true,
            [
                new DeskChildRequest("Kamo", "Khumalo", Today.AddYears(-1), null, "Asthma: pump in nappy bag", null),
                new DeskChildRequest("Lebo", "Khumalo", Today.AddYears(-11), null, null, null),
            ]))).ReadAsync<DeskCheckInResultDto>();

        Assert.Equal(2, result.Labels.Count);
        Assert.All(result.Labels, l => Assert.Equal(result.PickupCode, l.PickupCode));
        var kamo = result.Labels.Single(l => l.ChildName.StartsWith("Kamo", StringComparison.Ordinal));
        Assert.Equal("Little ones", kamo.ClassName);
        Assert.True(kamo.HasCareNotes);
        Assert.Equal("Pre-teens", result.Labels.Single(l => l.ChildName.StartsWith("Lebo", StringComparison.Ordinal)).ClassName);
        Assert.Equal(result.PickupCode, (await (await admin.GetAsync($"/api/admin/kids/check-ins/{kamo.CheckInId}/label")).ReadAsync<KidsLabelDto>()).PickupCode);

        // Without the parent's agreement nothing is kept.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostJsonAsync("/api/admin/kids/desk-check-in", new DeskCheckInRequest(
            "No", "Consent", "082 555 8122", Consent: false, [new DeskChildRequest("Child", "Consent", Today.AddYears(-5), null, null, null)]))).StatusCode);

        // A volunteer who runs check-in sees the flag but not the notes; a leader with the care permission can read them.
        var volunteer = await CreateStaffAsync(admin, "kids.desk@test.local", "Kids desk", ["kids.checkin"]);
        var today = await (await volunteer.GetAsync("/api/admin/kids/today")).ReadAsync<KidsTodayDto>();
        var kamoToday = today.Classes.SelectMany(c => c.Children).Single(c => c.CheckInId == kamo.CheckInId);
        Assert.True(kamoToday.HasCareNotes);
        Assert.Equal(HttpStatusCode.Forbidden, (await volunteer.GetAsync($"/api/admin/kids/children/{kamoToday.ChildId}/care-notes")).StatusCode);
        var notes = await (await admin.GetAsync($"/api/admin/kids/children/{kamoToday.ChildId}/care-notes")).ReadAsync<CareNotesDto>();
        Assert.Equal("Asthma: pump in nappy bag", notes.Medical);

        // Members and the signed-out can't reach the desk at all.
        var (member, _) = await SignInMemberAsync("082 555 8123", "+27825558123", "Zanele", "Mthembu");
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/admin/kids/today")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Browser().GetAsync("/api/admin/kids/today")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Browser().GetAsync("/api/me/kids")).StatusCode);
    }

    private async Task<HttpClient> CreateStaffAsync(HttpClient admin, string email, string roleName, string[] permissions)
    {
        var person = await (await admin.PostJsonAsync("/api/admin/people", new CreatePersonRequest("Staff", roleName, null, null, null, null, null, null, null))).ReadAsync<PersonDetailDto>();
        var setup = await (await admin.PostJsonAsync($"/api/admin/people/{person.Id}/staff-login", new { email })).ReadAsync<StaffLoginSetupDto>();
        const string password = "a-long-enough-password";
        (await admin.PostJsonAsync("/api/auth/staff/set-password", new { setup.UserId, token = setup.SetupToken, password })).EnsureSuccessStatusCode();
        var role = await (await admin.PostJsonAsync("/api/admin/roles", new SaveRoleRequest(roleName, null, permissions))).ReadAsync<RoleDto>();
        await (await admin.PostJsonAsync("/api/admin/grants", new CreateGrantRequest(person.Id, role.Id, "shapers", null, null))).ReadAsync<GrantDto>();
        return await api.SignInStaffAsync(email, password);
    }

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
            consents = new[] { new ConsentDecision(ConsentPurposes.ChurchRecord, true) },
        })).ReadAsync<SignInResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", signedIn.Tokens!.AccessToken);
        var profile = await (await client.GetAsync("/api/me/profile")).ReadAsync<PersonDetailDto>();
        return (client, profile.Id);
    }
}
