using System.Net;
using System.Net.Http.Headers;
using Shapers.Church.Application;
using Shapers.Events.Application;
using Shapers.Events.Domain;
using Shapers.Groups.Application;
using Shapers.Groups.Domain;
using Shapers.Identity.Application;
using Shapers.Media.Application;
using Shapers.People.Application;
using Shapers.People.Contracts;
using Shapers.People.Domain;
using Shapers.Platform.Calendar;
using Shapers.Services.Application;

namespace Shapers.IntegrationTests;

public sealed class CalendarTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task Visitors_members_and_staff_each_see_what_is_theirs_to_see()
    {
        var admin = await api.SignInAdminAsync();
        var inTenDays = DateTimeOffset.UtcNow.AddDays(10);

        var open = await CreateEventAsync(admin, "Calendar open day", EventVisibility.Public, publish: true, inTenDays);
        var membersOnly = await CreateEventAsync(admin, "Calendar members meeting", EventVisibility.Members, publish: true, inTenDays);
        var draft = await CreateEventAsync(admin, "Calendar planning draft", EventVisibility.Public, publish: false, inTenDays);
        var stream = await (await admin.PostJsonAsync("/api/admin/media/livestreams", new SaveLivestreamRequest(
            "Calendar stream", DateTimeOffset.UtcNow.AddDays(3), "https://www.youtube.com/live/y0jPz7KFw_o", null, null, null))).ReadAsync<LivestreamAdminDto>();
        var type = await (await admin.PostJsonAsync("/api/admin/services/types", new SaveServiceTypeRequest("Calendar Sunday", new TimeOnly(9, 0), [], [], null))).ReadAsync<ServiceTypeDto>();
        var plan = await (await admin.PostJsonAsync("/api/admin/services/plans", new CreatePlanRequest(type.Id, Today.AddDays(5), null, null, null))).ReadAsync<PlanDto>();

        var (member, memberId) = await SignInMemberAsync("082 555 7101", "+27825557101", "Naledi", "Mokoena");
        var (_, otherId) = await SignInMemberAsync("082 555 7102", "+27825557102", "Sipho", "Dube");
        var mine = await CreateCellAsync(admin, "Calendar Rivonia", DayOfWeek.Wednesday, memberId);
        var theirs = await CreateCellAsync(admin, "Calendar Sandton", DayOfWeek.Thursday, otherId);

        var range = $"from={Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("O"))}&to={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(21).ToString("O"))}";

        // A visitor: public events and livestreams only.
        var visitor = await (await api.Browser().GetAsync($"/api/calendar?{range}&allCells=true")).ReadAsync<List<CalendarEntry>>();
        Assert.Contains(visitor, e => e.RefId == open.Event.Id && e.Public);
        Assert.Contains(visitor, e => e.RefId == stream.Id && e.Kind == CalendarEntryKind.Livestream);
        Assert.DoesNotContain(visitor, e => e.RefId == membersOnly.Event.Id || e.RefId == draft.Event.Id || e.RefId == plan.Id);
        Assert.DoesNotContain(visitor, e => e.Kind is CalendarEntryKind.Cell or CalendarEntryKind.Service or CalendarEntryKind.Serving);

        // A member: members-only events and their own cell's weekly meeting, marked as theirs; nobody else's cell.
        var forMember = await (await member.GetAsync($"/api/calendar?{range}&allCells=true")).ReadAsync<List<CalendarEntry>>();
        Assert.Contains(forMember, e => e.RefId == membersOnly.Event.Id && !e.Public);
        var meetings = forMember.Where(e => e.RefId == mine.Id).ToList();
        Assert.InRange(meetings.Count, 3, 4);
        Assert.All(meetings, m => Assert.True(m.Mine && m.Kind == CalendarEntryKind.Cell && m.Place == "Rivonia"));
        Assert.All(meetings, m => Assert.Equal(DayOfWeek.Wednesday, TimeZoneInfo.ConvertTime(m.StartsAt, ChurchTime.Zone).DayOfWeek));
        Assert.DoesNotContain(forMember, e => e.RefId == theirs.Id || e.RefId == draft.Event.Id || e.RefId == plan.Id);

        // Staff: drafts and service plans; every cell only when asked for.
        var forStaff = await (await admin.GetAsync($"/api/calendar?{range}")).ReadAsync<List<CalendarEntry>>();
        Assert.Contains(forStaff, e => e.RefId == draft.Event.Id && e.Draft);
        Assert.Contains(forStaff, e => e.RefId == plan.Id && e.Kind == CalendarEntryKind.Service);
        Assert.DoesNotContain(forStaff, e => e.RefId == theirs.Id);
        var withCells = await (await admin.GetAsync($"/api/calendar?{range}&allCells=true")).ReadAsync<List<CalendarEntry>>();
        Assert.Contains(withCells, e => e.RefId == theirs.Id && !e.Mine);

        // The feed people subscribe to holds only public entries, whoever asks for it.
        var feed = await (await admin.GetAsync("/calendar.ics")).Content.ReadAsStringAsync();
        Assert.StartsWith("BEGIN:VCALENDAR", feed, StringComparison.Ordinal);
        Assert.Contains("SUMMARY:Calendar open day", feed, StringComparison.Ordinal);
        Assert.Contains("SUMMARY:Calendar stream (online)", feed, StringComparison.Ordinal);
        foreach (var hidden in new[] { "Calendar members meeting", "Calendar planning draft", "Calendar Sunday", "Calendar Rivonia", "Calendar Sandton" })
        {
            Assert.DoesNotContain(hidden, feed, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_range_longer_than_three_months_or_backwards_is_refused()
    {
        var client = api.Browser();
        var now = DateTimeOffset.UtcNow;

        var tooLong = await client.GetAsync($"/api/calendar?from={Uri.EscapeDataString(now.ToString("O"))}&to={Uri.EscapeDataString(now.AddDays(120).ToString("O"))}");
        var backwards = await client.GetAsync($"/api/calendar?from={Uri.EscapeDataString(now.ToString("O"))}&to={Uri.EscapeDataString(now.AddDays(-1).ToString("O"))}");

        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, backwards.StatusCode);
    }

    private static async Task<EventAdminDto> CreateEventAsync(HttpClient admin, string title, EventVisibility visibility, bool publish, DateTimeOffset startsAt)
    {
        var e = await (await admin.PostJsonAsync("/api/admin/events", new SaveEventRequest(
            title, "A summary", null, startsAt, startsAt.AddHours(2), new LocationDto("Shapers Church", "8 Mellis Road, Rivonia"), null,
            visibility, false, null, null, null, false, 4, [], null))).ReadAsync<EventAdminDto>();
        return publish ? await (await admin.PostAsync($"/api/admin/events/{e.Event.Id}/publish", null)).ReadAsync<EventAdminDto>() : e;
    }

    private static async Task<CellDetailDto> CreateCellAsync(HttpClient admin, string name, DayOfWeek day, Guid memberId)
    {
        var rivonia = (await (await admin.GetAsync("/api/admin/campuses")).ReadAsync<List<CampusDto>>()).Single(c => c.IsPrimary);
        var cell = await (await admin.PostJsonAsync("/api/admin/cells", new SaveCellRequest(name, rivonia.Id, day, new TimeOnly(19, 0), name.Split(' ')[1], "12 Example Road"))).ReadAsync<CellDetailDto>();
        return await (await admin.PostJsonAsync($"/api/admin/cells/{cell.Id}/members", new AddMemberRequest(memberId, CellRole.Member))).ReadAsync<CellDetailDto>();
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
