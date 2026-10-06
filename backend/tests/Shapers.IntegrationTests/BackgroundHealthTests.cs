using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shapers.Api.Hosting;
using Shapers.Identity.Application;
using Shapers.People.Application;
using Shapers.Prayer.Infrastructure;

namespace Shapers.IntegrationTests;

public sealed class BackgroundHealthTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Staff_with_the_jobs_permission_see_messages_that_are_stuck_or_given_up()
    {
        var admin = await api.SignInAdminAsync();
        var waiting = Guid.CreateVersion7();
        var gaveUp = Guid.CreateVersion7();
        await using (var scope = api.Services.CreateAsyncScope())
        {
            // An unknown event type, so the running outbox processor can't deliver either message.
            var db = scope.ServiceProvider.GetRequiredService<PrayerDbContext>();
            await db.Database.ExecuteSqlAsync($$"""
                INSERT INTO prayer.outbox_messages (id, type, payload, occurred_at, attempts, last_error) VALUES
                ({{waiting}}, 'tests.never-delivered', '{}', now() - interval '1 hour', 0, NULL),
                ({{gaveUp}}, 'tests.never-delivered', '{}', now() - interval '2 hours', 10, 'System.InvalidOperationException: jane@example.org not found')
                """);
        }

        var health = await (await admin.GetAsync("/api/admin/background-health")).ReadAsync<BackgroundHealthDto>();

        Assert.True(health.NeedsAttention);
        Assert.Null(health.Jobs); // Background jobs are switched off in tests.
        var prayer = Assert.Single(health.Outbox, o => o.Module == "prayer");
        Assert.True(prayer.Waiting >= 1);
        Assert.Equal(1, prayer.GaveUp);
        var problem = Assert.Single(prayer.Oldest, p => p.Id == gaveUp);
        Assert.True(problem.GaveUp);
        Assert.Equal("InvalidOperationException", problem.ErrorType);
        Assert.Contains(prayer.Oldest, p => p.Id == waiting && !p.GaveUp);

        // The error message, which could hold someone's details, never leaves the database.
        var raw = await (await admin.GetAsync("/api/admin/background-health")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("jane@example.org", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Staff_without_the_jobs_permission_are_refused()
    {
        var admin = await api.SignInAdminAsync();
        var person = await (await admin.PostJsonAsync("/api/admin/people", new CreatePersonRequest("Staff", "Sermons", null, null, null, null, null, null, null))).ReadAsync<PersonDetailDto>();
        var setup = await (await admin.PostJsonAsync($"/api/admin/people/{person.Id}/staff-login", new { email = "no.jobs@test.local" })).ReadAsync<StaffLoginSetupDto>();
        const string password = "a-long-enough-password";
        (await admin.PostJsonAsync("/api/auth/staff/set-password", new { setup.UserId, token = setup.SetupToken, password })).EnsureSuccessStatusCode();
        var role = await (await admin.PostJsonAsync("/api/admin/roles", new SaveRoleRequest("Sermons only", null, ["media.sermons.edit"]))).ReadAsync<RoleDto>();
        await (await admin.PostJsonAsync("/api/admin/grants", new CreateGrantRequest(person.Id, role.Id, "shapers", null, null))).ReadAsync<GrantDto>();
        var editor = await api.SignInStaffAsync("no.jobs@test.local", password);

        Assert.Equal(HttpStatusCode.Forbidden, (await editor.GetAsync("/api/admin/background-health")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Browser().GetAsync("/api/admin/background-health")).StatusCode);
    }
}
