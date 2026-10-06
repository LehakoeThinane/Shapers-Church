using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Shapers.Church.Application;
using Shapers.Communications.Application;
using Shapers.Groups.Application;
using Shapers.Groups.Domain;
using Shapers.Identity.Api;
using Shapers.Identity.Application;
using Shapers.People.Application;
using Shapers.People.Contracts;
using Shapers.People.Domain;

namespace Shapers.IntegrationTests;

public sealed class CellsTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static readonly ConsentDecision[] Consents = [new(ConsentPurposes.ChurchRecord, true)];
    private static readonly DateOnly LastTuesday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-2);

    [Fact]
    public async Task A_leader_reports_on_a_meeting_and_the_pastors_see_it_with_visitors_followed_up()
    {
        var admin = await api.SignInAdminAsync();
        var (leader, leaderId) = await SignInMemberAsync("082 555 6001", "+27825556001", "Sipho", "Ndlovu");
        var (_, memberId) = await SignInMemberAsync("082 555 6002", "+27825556002", "Lerato", "Mokoena");
        var cell = await CreateCellAsync(admin, "Rivonia North", (leaderId, CellRole.Leader), (memberId, CellRole.Member));

        var draft = await (await leader.PostJsonAsync($"/api/cells/{cell.Id}/reports", Report(LastTuesday, submit: false) with
        {
            AttendeeIds = [leaderId, memberId],
            Visitors = [new ReportVisitor("Thabo", "Dlamini", "082 555 6099", null, true), new ReportVisitor("Naledi", "Khumalo", "082 000 0000", null, false)],
            FollowUps = [new ReportFollowUp(Guid.Empty, memberId, "Lerato", "Lost her job this week.", Urgent: true)],
            PrayerNeeds = "Lerato's job search.",
        })).ReadAsync<ReportDto>();
        Assert.Equal(ReportStatus.Draft, draft.Status);

        // Drafts stay with the leaders: pastors only see submitted reports.
        Assert.DoesNotContain(await (await admin.GetAsync("/api/admin/cells/reports")).ReadAsync<List<ReportSummaryDto>>(), r => r.Id == draft.Id);

        var submitted = await (await leader.PutAsJsonAsync($"/api/cells/{cell.Id}/reports/{draft.Id}", Report(LastTuesday, submit: true) with
        {
            AttendeeIds = [leaderId, memberId],
            Visitors = draft.Visitors,
            FollowUps = draft.FollowUps,
            PrayerNeeds = draft.PrayerNeeds,
        }, ApiFactory.Json)).ReadAsync<ReportDto>();
        Assert.Equal(ReportStatus.Submitted, submitted.Status);
        Assert.Equal(2, submitted.MembersPresent);

        // The visitor who didn't agree is only a first name in the report.
        var naledi = Assert.Single(submitted.Visitors, v => v.FirstName == "Naledi");
        Assert.Null(naledi.Mobile);

        // Pastors see it, with the urgent follow-up counted on the cell.
        var reports = await (await admin.GetAsync("/api/admin/cells/reports")).ReadAsync<List<ReportSummaryDto>>();
        var summary = Assert.Single(reports, r => r.Id == draft.Id);
        Assert.Equal(1, summary.OpenUrgentFollowUps);
        var full = await (await admin.GetAsync($"/api/admin/cells/reports/{draft.Id}")).ReadAsync<ReportDto>();
        Assert.Equal("Lerato's job search.", full.PrayerNeeds);
        Assert.Contains(full.Attendees, a => a.Name == "Lerato Mokoena");
        var overview = Assert.Single(await (await admin.GetAsync("/api/admin/cells")).ReadAsync<List<CellSummaryDto>>(), c => c.Id == cell.Id);
        Assert.True(overview.ReportedThisWeek);
        Assert.Equal(1, overview.OpenUrgentFollowUps);

        // The visitor who agreed becomes a connect card; the other never leaves the report.
        var card = await EventuallyAsync(async () =>
            (await (await admin.GetAsync("/api/admin/connect-cards")).ReadAsync<List<ConnectCardDto>>()).FirstOrDefault(c => c.Source == "cell" && c.PersonName.StartsWith("Thabo", StringComparison.Ordinal)));
        Assert.Contains("Rivonia North", card.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(await (await admin.GetAsync("/api/admin/connect-cards")).ReadAsync<List<ConnectCardDto>>(), c => c.PersonName.StartsWith("Naledi", StringComparison.Ordinal));

        // The pastors are alerted to the urgent follow-up, without any names in the alert.
        var alert = await EventuallyAsync(async () =>
            (await (await admin.GetAsync("/api/me/notifications")).ReadAsync<InboxDto>()).Items.FirstOrDefault(n => n.Title == "Urgent follow-up from a cell"));
        Assert.DoesNotContain("Lerato", alert.Body, StringComparison.Ordinal);

        // A pastor resolves it.
        var resolved = await (await admin.PostAsync($"/api/admin/cells/reports/{draft.Id}/follow-ups/{full.FollowUps[0].Id}/resolve", null)).ReadAsync<ReportDto>();
        Assert.NotNull(resolved.FollowUps[0].ResolvedAt);

        // Submitted reports are locked, and one report per meeting.
        Assert.Equal(HttpStatusCode.BadRequest, (await leader.PutAsJsonAsync($"/api/cells/{cell.Id}/reports/{draft.Id}", Report(LastTuesday, submit: false), ApiFactory.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PostJsonAsync($"/api/cells/{cell.Id}/reports", Report(LastTuesday, submit: false))).StatusCode);
    }

    [Fact]
    public async Task Leaders_see_only_their_own_cell_and_members_see_only_what_is_shared()
    {
        var admin = await api.SignInAdminAsync();
        var (leader, leaderId) = await SignInMemberAsync("082 555 6011", "+27825556011", "Kagiso", "Mahlangu");
        var (member, memberId) = await SignInMemberAsync("082 555 6012", "+27825556012", "Zanele", "Dube");
        var (otherLeader, otherLeaderId) = await SignInMemberAsync("082 555 6013", "+27825556013", "Pieter", "Botha");
        var cell = await CreateCellAsync(admin, "Sandton East", (leaderId, CellRole.Leader), (memberId, CellRole.Member));
        var other = await CreateCellAsync(admin, "Fourways", (otherLeaderId, CellRole.Leader));

        // Another cell's leader, and the cell's own members, can't open it as a leader.
        Assert.Equal(HttpStatusCode.NotFound, (await otherLeader.GetAsync($"/api/cells/{cell.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync($"/api/cells/{cell.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await leader.GetAsync($"/api/cells/{other.Id}")).StatusCode);

        // Members and leaders can't reach the pastors' screens.
        Assert.Equal(HttpStatusCode.Forbidden, (await leader.GetAsync("/api/admin/cells/reports")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/admin/cells")).StatusCode);

        // A leader can't remove another leader, or report someone outside the cell as present.
        Assert.Equal(HttpStatusCode.Forbidden, (await leader.DeleteAsync($"/api/cells/{cell.Id}/members/{leaderId}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await leader.PostJsonAsync($"/api/cells/{cell.Id}/reports", Report(LastTuesday, false) with { AttendeeIds = [otherLeaderId] })).StatusCode);

        // Materials: shared ones reach members; private ones stay with leaders and pastors.
        (await leader.PostJsonAsync($"/api/cells/{cell.Id}/materials", new SaveMaterialRequest("Week 1: Purpose", "Read Ephesians 2:10 together.", null, null, true))).EnsureSuccessStatusCode();
        (await leader.PostJsonAsync($"/api/cells/{cell.Id}/materials", new SaveMaterialRequest("Leader notes", "Watch for who is quiet.", null, null, false))).EnsureSuccessStatusCode();

        var mine = Assert.Single(await (await member.GetAsync("/api/me/cells")).ReadAsync<List<MyCellDto>>());
        Assert.Equal("Sandton East", mine.Name);
        Assert.Equal(CellRole.Member, mine.MyRole);
        Assert.Equal("Kagiso Mahlangu", Assert.Single(mine.Leaders));
        Assert.Equal("Week 1: Purpose", Assert.Single(mine.SharedMaterials).Title);

        var pastorView = await (await admin.GetAsync($"/api/admin/cells/materials?cellId={cell.Id}")).ReadAsync<List<MaterialDto>>();
        Assert.Equal(2, pastorView.Count);

        // A leader adds someone new: a new church record, as a member (never a leader).
        var added = await (await leader.PostJsonAsync($"/api/cells/{cell.Id}/members", new NewMemberRequest("Ayanda", "Zulu", "082 555 6019", null, true))).ReadAsync<CellDetailDto>();
        Assert.Contains(added.Members, m => m.Name == "Ayanda Zulu" && m.Role == CellRole.Member);
    }

    [Fact]
    public async Task A_pastor_reading_a_report_is_recorded_in_the_audit_log()
    {
        var admin = await api.SignInAdminAsync();
        var (leader, leaderId) = await SignInMemberAsync("082 555 6021", "+27825556021", "Nomsa", "Sithole");
        var cell = await CreateCellAsync(admin, "Morningside", (leaderId, CellRole.Leader));
        var report = await (await leader.PostJsonAsync($"/api/cells/{cell.Id}/reports", Report(LastTuesday, submit: true) with { AttendeeIds = [leaderId] })).ReadAsync<ReportDto>();

        (await admin.GetAsync($"/api/admin/cells/reports/{report.Id}")).EnsureSuccessStatusCode();

        var audit = await (await admin.GetAsync($"/api/admin/audit?entityType=cell_report&entityId={report.Id}")).Content.ReadAsStringAsync();
        Assert.Contains("groups.report.viewed", audit, StringComparison.Ordinal);
    }

    private static SaveReportRequest Report(DateOnly date, bool submit) =>
        new(date, "Purpose", null, "Good discussion.", null, null, MultiplicationReadiness.NotYet, [], [], [], [], submit);

    private static async Task<CellDetailDto> CreateCellAsync(HttpClient admin, string name, params (Guid PersonId, CellRole Role)[] people)
    {
        var rivonia = (await (await admin.GetAsync("/api/admin/campuses")).ReadAsync<List<CampusDto>>()).Single(c => c.IsPrimary);
        var cell = await (await admin.PostJsonAsync("/api/admin/cells", new SaveCellRequest(name, rivonia.Id, DayOfWeek.Tuesday, new TimeOnly(19, 0), "Rivonia", "12 Example Road"))).ReadAsync<CellDetailDto>();
        foreach (var (personId, role) in people)
        {
            cell = await (await admin.PostJsonAsync($"/api/admin/cells/{cell.Id}/members", new AddMemberRequest(personId, role))).ReadAsync<CellDetailDto>();
        }

        return cell;
    }

    private static async Task<T> EventuallyAsync<T>(Func<Task<T?>> probe)
        where T : class
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (await probe() is { } found)
            {
                return found;
            }

            await Task.Delay(250, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException("The expected result didn't appear.");
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
            consents = Consents,
        })).ReadAsync<SignInResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", signedIn.Tokens!.AccessToken);
        var profile = await (await client.GetAsync("/api/me/profile")).ReadAsync<PersonDetailDto>();
        return (client, profile.Id);
    }
}

/// <summary>With two-step sign-in required, a leader signed in by phone code alone can't open the cell.</summary>
public sealed class CellLeaderMfaTests(MfaApiFactory api) : IClassFixture<MfaApiFactory>
{
    [Fact]
    public async Task A_leader_without_a_second_factor_is_asked_to_set_one_up()
    {
        var admin = await api.SignInAdminAsync();
        var setup = await (await admin.PostAsync("/api/auth/2fa/setup", null)).ReadAsync<AuthenticatorSetup>();
        await (await admin.PostJsonAsync("/api/auth/2fa/enable", new { code = Totp.Now(setup.SharedKey.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant()) }))
            .ReadAsync<RecoveryCodes>();

        var client = api.Browser();
        var request = await (await client.PostJsonAsync("/api/auth/otp/request", new { phone = "082 555 6031" })).ReadAsync<RequestCodeResponse>();
        var verified = await (await client.PostJsonAsync("/api/auth/otp/verify", new { request.ChallengeId, code = api.Sms.LastCodeFor("+27825556031") })).ReadAsync<SignInResponse>();
        var signedIn = await (await client.PostJsonAsync("/api/auth/register", new
        {
            request.ChallengeId,
            registrationTicket = verified.RegistrationTicket,
            firstName = "Thandi",
            lastName = "Mthembu",
            policyVersion = "2026-09",
            consents = new[] { new ConsentDecision(ConsentPurposes.ChurchRecord, true) },
        })).ReadAsync<SignInResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", signedIn.Tokens!.AccessToken);
        var profile = await (await client.GetAsync("/api/me/profile")).ReadAsync<PersonDetailDto>();

        var rivonia = (await (await admin.GetAsync("/api/admin/campuses")).ReadAsync<List<CampusDto>>()).Single(c => c.IsPrimary);
        var cell = await (await admin.PostJsonAsync("/api/admin/cells", new SaveCellRequest("Bryanston", rivonia.Id, null, null, null, null))).ReadAsync<CellDetailDto>();
        (await admin.PostJsonAsync($"/api/admin/cells/{cell.Id}/members", new AddMemberRequest(profile.Id, CellRole.Leader))).EnsureSuccessStatusCode();

        var refused = await client.GetAsync($"/api/cells/{cell.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Contains("groups.mfa_required", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // Their own cell still shows in the member view.
        Assert.Single(await (await client.GetAsync("/api/me/cells")).ReadAsync<List<MyCellDto>>());
    }
}
