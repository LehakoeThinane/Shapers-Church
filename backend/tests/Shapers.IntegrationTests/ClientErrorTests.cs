using System.Net;
using System.Net.Http.Json;
using System.Text;
using Shapers.Api.Hosting;

namespace Shapers.IntegrationTests;

public sealed class ClientErrorTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task A_crash_is_logged_without_anything_that_identifies_a_person()
    {
        var report = new ClientErrorReport(
            "admin",
            "2026.10.07",
            "/people/0199b9a0-0000-7000-8000-00000000a001?search=thandi",
            "TypeError",
            "Cannot read 'name' of thandi@example.com (+27 82 123 4567) at https://admin.test/people?token=abc",
            "TypeError: x\n    at PersonPage (https://admin.test/assets/index-Ab12Cd34.js?v=1:12:345)\n    at eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.c2lnbmF0dXJl");

        // Signed out: a crash on the sign-in page must still be reported.
        var response = await api.Browser().PostAsJsonAsync("/api/client-errors", report);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var line = Assert.Single(api.ClientErrorLog.Lines, l => l.Contains("Cannot read", StringComparison.Ordinal));
        Assert.StartsWith("Warning: Client error in admin 2026.10.07 at /people/:id: TypeError:", line, StringComparison.Ordinal);
        Assert.Contains("[email]", line, StringComparison.Ordinal);
        Assert.Contains("[number]", line, StringComparison.Ordinal);
        Assert.Contains("index-Ab12Cd34.js:12:345", line, StringComparison.Ordinal);
        foreach (var personal in new[] { "thandi", "123 4567", "token=abc", "search=", "eyJ", "0199b9a0" })
        {
            Assert.DoesNotContain(personal, line, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Reports_from_unknown_apps_and_oversized_reports_are_refused()
    {
        var client = api.Browser();

        var unknown = await client.PostAsJsonAsync("/api/client-errors", new ClientErrorReport("someone-else", null, null, null, "hello", null));
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);

        var huge = new ClientErrorReport("member-app", null, null, null, new string('x', 20_000), null);
        var oversized = await client.PostAsJsonAsync("/api/client-errors", huge);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);

        var notJson = await client.PostAsync("/api/client-errors", new StringContent("not json", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, notJson.StatusCode);

        Assert.DoesNotContain(api.ClientErrorLog.Lines, l => l.Contains("hello", StringComparison.Ordinal) || l.Contains("xxxx", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("/people/0199b9a0-0000-7000-8000-00000000a001/edit", "/people/:id/edit")]
    [InlineData("/my-cells/42/reports/7?draft=1#top", "/my-cells/:id/reports/:id")]
    [InlineData("sermon/[slug]", "sermon/[slug]")]
    public void Routes_name_the_page_not_the_record(string route, string expected) =>
        Assert.Equal(expected, ClientErrorScrubber.Route(route));
}
