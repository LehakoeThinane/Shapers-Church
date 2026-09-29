using System.Net;
using System.Net.Http.Headers;
using Shapers.Identity.Application;
using Shapers.People.Application;
using Shapers.People.Contracts;
using Shapers.People.Domain;

namespace Shapers.IntegrationTests;

public sealed class MemberSignInTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static readonly ConsentDecision[] Consents =
    [
        new(ConsentPurposes.ChurchRecord, true),
        new(ConsentPurposes.WhatsAppCommunication, true),
    ];

    [Fact]
    public async Task New_member_verifies_phone_registers_and_sees_their_profile()
    {
        var client = api.Browser();

        var (challenge, ticket) = await VerifyNewNumberAsync(client, "082 555 0001", "+27825550001");
        var signedIn = await client.PostJsonAsync("/api/auth/register", Register(challenge, ticket, "Lerato", "Dlamini"))
            .ContinueWith(t => t.Result.ReadAsync<SignInResponse>()).Unwrap();

        Assert.Equal(SignInStatus.SignedIn, signedIn.Status);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", signedIn.Tokens!.AccessToken);
        var profile = await (await client.GetAsync("/api/me/profile")).ReadAsync<PersonDetailDto>();
        Assert.Equal("Lerato Dlamini", profile.DisplayName);
        Assert.Equal(PersonSource.SelfRegistration, profile.Source);
        Assert.Contains(profile.Contacts, c => c.Value == "+27825550001" && c.IsVerified);
        Assert.Contains(profile.Consents, c => c.Purpose == ConsentPurposes.ChurchRecord && c.Granted);

        // Members have no staff access.
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/people")).StatusCode);
    }

    [Fact]
    public async Task Registration_requires_consent_to_keep_a_church_record()
    {
        var client = api.Browser();
        var (challenge, ticket) = await VerifyNewNumberAsync(client, "082 555 0002", "+27825550002");

        var response = await client.PostJsonAsync("/api/auth/register", Register(challenge, ticket, "No", "Consent", consents: []));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("people.consent_required", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Wrong_codes_lock_the_challenge()
    {
        var client = api.Browser();
        var request = await (await client.PostJsonAsync("/api/auth/otp/request", new { phone = "082 555 0003" })).ReadAsync<RequestCodeResponse>();
        var code = api.Sms.LastCodeFor("+27825550003");

        for (var i = 0; i < 5; i++)
        {
            await client.PostJsonAsync("/api/auth/otp/verify", new { request.ChallengeId, code = "000000" });
        }

        var response = await client.PostJsonAsync("/api/auth/otp/verify", new { request.ChallengeId, code });
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Fact]
    public async Task Reusing_a_rotated_refresh_token_ends_the_whole_session()
    {
        var client = api.Browser();
        var (challenge, ticket) = await VerifyNewNumberAsync(client, "082 555 0004", "+27825550004");
        var first = (await (await client.PostJsonAsync("/api/auth/register", Register(challenge, ticket, "Sipho", "Nkosi"))).ReadAsync<SignInResponse>()).Tokens!;

        var second = await (await client.PostJsonAsync("/api/auth/refresh", new { first.RefreshToken })).ReadAsync<TokenPair>();
        Assert.NotEqual(first.RefreshToken, second.RefreshToken);

        var replay = await client.PostJsonAsync("/api/auth/refresh", new { first.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        var afterReplay = await client.PostJsonAsync("/api/auth/refresh", new { second.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, afterReplay.StatusCode);
    }

    [Fact]
    public async Task Member_is_linked_to_the_record_staff_already_created_for_their_number()
    {
        var admin = await api.SignInAdminAsync();
        var existing = await (await admin.PostJsonAsync("/api/admin/people", new CreatePersonRequest("Naledi", "Khumalo", null, null, null, null, null, null, "082 555 0005")))
            .ReadAsync<PersonDetailDto>();

        var client = api.Browser();
        var request = await (await client.PostJsonAsync("/api/auth/otp/request", new { phone = "0825550005" })).ReadAsync<RequestCodeResponse>();
        var verified = await (await client.PostJsonAsync("/api/auth/otp/verify", new { request.ChallengeId, code = api.Sms.LastCodeFor("+27825550005") }))
            .ReadAsync<SignInResponse>();
        Assert.True(verified.ExistingRecordFound);

        var registered = await (await client.PostJsonAsync("/api/auth/register", Register(request.ChallengeId, verified.RegistrationTicket!, "Naledi", "Khumalo")))
            .ReadAsync<SignInResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registered.Tokens!.AccessToken);
        var profile = await (await client.GetAsync("/api/me/profile")).ReadAsync<PersonDetailDto>();

        Assert.Equal(existing.Id, profile.Id);
    }

    private async Task<(Guid ChallengeId, string Ticket)> VerifyNewNumberAsync(HttpClient client, string phone, string e164)
    {
        var request = await (await client.PostJsonAsync("/api/auth/otp/request", new { phone })).ReadAsync<RequestCodeResponse>();
        var verified = await (await client.PostJsonAsync("/api/auth/otp/verify", new { request.ChallengeId, code = api.Sms.LastCodeFor(e164) }))
            .ReadAsync<SignInResponse>();
        Assert.Equal(SignInStatus.RegistrationRequired, verified.Status);
        return (request.ChallengeId, verified.RegistrationTicket!);
    }

    private static object Register(Guid challengeId, string ticket, string first, string last, ConsentDecision[]? consents = null) => new
    {
        challengeId,
        registrationTicket = ticket,
        firstName = first,
        lastName = last,
        policyVersion = "2026-09",
        consents = consents ?? Consents,
    };
}
