using System.Net;
using Shapers.People.Application;
using Shapers.People.Domain;

namespace Shapers.IntegrationTests;

public sealed class EmailConsentTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task People_are_asked_once_and_their_answer_becomes_their_consent()
    {
        var admin = await api.SignInAdminAsync();
        var yes = await (await admin.PostJsonAsync("/api/admin/people", new CreatePersonRequest("Thato", "Agrees", null, null, null, null, null, "thato.agrees@example.com", null)))
            .ReadAsync<PersonDetailDto>();
        var no = await (await admin.PostJsonAsync("/api/admin/people", new CreatePersonRequest("Nandi", "Declines", null, null, null, null, null, "nandi.declines@example.com", null)))
            .ReadAsync<PersonDetailDto>();

        var before = await (await admin.GetAsync("/api/admin/people/email-consent")).ReadAsync<EmailConsentSummary>();
        Assert.True(before.CanBeAsked >= 2);
        (await admin.PostAsync("/api/admin/people/email-consent/send", null)).EnsureSuccessStatusCode();

        var browser = api.Browser();
        await AnswerAsync(browser, "thato.agrees@example.com", "yes");
        await AnswerAsync(browser, "nandi.declines@example.com", "no");

        var agreed = await (await admin.GetAsync($"/api/admin/people/{yes.Id}")).ReadAsync<PersonDetailDto>();
        var consent = Assert.Single(agreed.Consents, c => c.Purpose == ConsentPurposes.EmailCommunication);
        Assert.True(consent.Granted);
        Assert.Equal(ConsentSource.EmailRequest, consent.Source);
        var declined = await (await admin.GetAsync($"/api/admin/people/{no.Id}")).ReadAsync<PersonDetailDto>();
        Assert.False(Assert.Single(declined.Consents, c => c.Purpose == ConsentPurposes.EmailCommunication).Granted);

        // Nobody is asked twice.
        var sentBefore = api.Email.Sent.Count;
        var again = await (await admin.PostAsync("/api/admin/people/email-consent/send", null)).ReadAsync<EmailConsentSendResult>();
        Assert.Equal(0, again.Sent);
        Assert.Equal(sentBefore, api.Email.Sent.Count);

        // A forged link does nothing.
        var forged = await browser.GetAsync($"{EmailConsentRequests.AnswerPath}?p={no.Id:N}&s=00000000000000000000000000000000");
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
    }

    private async Task AnswerAsync(HttpClient browser, string email, string answer)
    {
        var message = Assert.Single(api.Email.Sent, m => m.To == email);
        var link = new Uri(message.PlainText[(message.PlainText.LastIndexOf("Yes or no: ", StringComparison.Ordinal) + "Yes or no: ".Length)..].Trim());
        var query = System.Web.HttpUtility.ParseQueryString(link.Query);

        // Opening the link only shows the choice.
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync(link.PathAndQuery)).StatusCode);
        var answered = await browser.PostAsync(EmailConsentRequests.AnswerPath, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["p"] = query["p"]!,
            ["s"] = query["s"]!,
            ["answer"] = answer,
        }));
        Assert.Equal(HttpStatusCode.OK, answered.StatusCode);
    }
}
