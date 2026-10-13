using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shapers.Giving.Application;
using Shapers.Giving.Domain;
using Shapers.Identity.Application;
using Shapers.People.Application;
using Shapers.People.Contracts;
using Shapers.People.Domain;

namespace Shapers.IntegrationTests;

public sealed class GivingTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly WebApplicationFactory<Program> _app = api.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton<IPaymentProvider, FakePaymentProvider>()));

    [Fact]
    public async Task A_visitor_gives_by_card_and_only_the_providers_signed_word_marks_it_received()
    {
        var visitor = Client();
        var page = await (await visitor.GetAsync("/api/giving")).ReadAsync<GivingPageDto>();
        Assert.True(page.CardGivingEnabled);
        Assert.Equal("Standard Bank", page.Eft.Bank);
        var tithe = Assert.Single(page.Funds, f => f.Name == "Tithe");

        // A visitor gives a name and email for the receipt; without them, nothing starts.
        Assert.Equal(HttpStatusCode.BadRequest, (await visitor.PostJsonAsync("/api/giving/checkout", new StartGiftRequest(tithe.Id, 250m, "Naledi", null))).StatusCode);
        var started = await (await visitor.PostJsonAsync("/api/giving/checkout", new StartGiftRequest(tithe.Id, 250m, "Naledi Khumalo", "naledi@example.org"))).ReadAsync<StartGiftResponse>();
        Assert.StartsWith("https://pay.example/", started.RedirectUrl, StringComparison.Ordinal);

        // An unsigned notice is refused; the signed one marks the gift received, and repeating it changes nothing.
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.PostAsync("/api/giving/webhooks/yoco", Notice(started.GiftId, 25_000, signed: false))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await visitor.PostAsync("/api/giving/webhooks/yoco", Notice(started.GiftId, 25_000, signed: true))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await visitor.PostAsync("/api/giving/webhooks/yoco", Notice(started.GiftId, 25_000, signed: true))).StatusCode);

        var admin = await SignInAdminAsync();
        var gifts = await (await admin.GetAsync("/api/admin/giving/gifts?search=naledi")).ReadAsync<GiftPageDto>();
        var gift = Assert.Single(gifts.Items);
        Assert.Equal(25_000, gift.AmountCents);
        Assert.Equal(GiftMethod.Card, gift.Method);
        Assert.Equal("Naledi Khumalo", gift.GiverName);
        // The receipt address is never listed.
        Assert.DoesNotContain("naledi@example.org", await (await admin.GetAsync("/api/admin/giving/gifts")).Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_member_sees_their_own_giving_and_the_finance_team_records_eft_and_prints_a_statement()
    {
        var (member, memberId) = await SignInMemberAsync("082 555 9301", "+27825559301", "Sipho", "Ndlovu");
        var offering = (await (await member.GetAsync("/api/giving")).ReadAsync<GivingPageDto>()).Funds.Single(f => f.Name == "Offering");
        var started = await (await member.PostJsonAsync("/api/giving/checkout", new StartGiftRequest(offering.Id, 100m, null, null))).ReadAsync<StartGiftResponse>();
        await Client().PostAsync("/api/giving/webhooks/yoco", Notice(started.GiftId, 10_000, signed: true));

        var admin = await SignInAdminAsync();
        var tithe = (await (await admin.GetAsync("/api/admin/giving/funds")).ReadAsync<List<FundDto>>()).Single(f => f.Name == "Tithe");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        (await admin.PostJsonAsync("/api/admin/giving/gifts", new RecordGiftRequest(tithe.Id, 1_500m, GiftMethod.Eft, memberId, null, "Bank statement 3 Oct", today))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostJsonAsync("/api/admin/giving/gifts", new RecordGiftRequest(tithe.Id, 50m, GiftMethod.Card, memberId, null, null, today))).StatusCode);

        var mine = await (await member.GetAsync("/api/me/giving")).ReadAsync<List<MyGiftDto>>();
        Assert.Equal(2, mine.Count);
        Assert.Equal(160_000, mine.Sum(g => g.AmountCents));

        var statement = await (await admin.GetAsync($"/api/admin/giving/statements/{memberId}?year={today.Year}")).ReadAsync<GivingStatementDto>();
        Assert.Equal("Sipho Ndlovu", statement.PersonName);
        Assert.Equal(160_000, statement.TotalCents);
        Assert.Equal(2, statement.ByFund.Count);

        var overview = await (await admin.GetAsync("/api/admin/giving/overview")).ReadAsync<GivingOverviewDto>();
        Assert.True(overview.TotalCents >= 160_000);
        Assert.Contains(overview.ByMethod, m => m.Label == "EFT");
    }

    [Fact]
    public async Task Only_staff_with_the_giving_permission_see_who_gave()
    {
        var (member, _) = await SignInMemberAsync("082 555 9311", "+27825559311", "Zanele", "Dube");
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/admin/giving/overview")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/admin/giving/gifts")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client().GetAsync("/api/admin/giving/overview")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client().GetAsync("/api/me/giving")).StatusCode);
    }

    private HttpClient Client() => _app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });

    private static StringContent Notice(Guid giftId, long cents, bool signed)
    {
        var content = new StringContent($"{FakePaymentProvider.CheckoutFor(giftId)}|{cents}|ok", Encoding.UTF8, "text/plain");
        if (signed)
        {
            content.Headers.Add(FakePaymentProvider.SignatureHeader, "valid");
        }

        return content;
    }

    private async Task<HttpClient> SignInAdminAsync()
    {
        var client = Client();
        (await client.PostAsJsonAsync("/api/auth/staff/login", new { email = ApiFactory.AdminEmail, password = ApiFactory.AdminPassword })).EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Add(ApiFactory.Csrf, "1");
        return client;
    }

    private async Task<(HttpClient Client, Guid PersonId)> SignInMemberAsync(string phone, string e164, string first, string last)
    {
        var client = Client();
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

/// <summary>Stands in for Yoco: checkouts get a predictable ID, and a notice counts only with the test signature header.</summary>
public sealed class FakePaymentProvider : IPaymentProvider
{
    public const string SignatureHeader = "x-test-signature";

    public bool IsConfigured => true;

    public static string CheckoutFor(Guid giftId) => $"ch_{giftId:N}";

    public Task<CheckoutStarted> StartCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new CheckoutStarted(CheckoutFor(request.GiftId), $"https://pay.example/{CheckoutFor(request.GiftId)}"));

    public PaymentNotice? ReadNotice(IReadOnlyDictionary<string, string> headers, string body)
    {
        if (!headers.TryGetValue(SignatureHeader, out var signature) || signature != "valid")
        {
            return null;
        }

        var parts = body.Split('|');
        return new PaymentNotice(parts[0], "p_test", long.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture), parts[2] == "ok");
    }
}
