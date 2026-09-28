using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shapers.Church.Application;
using Shapers.Identity.Application;
using Shapers.Identity.Infrastructure;
using Shapers.People.Application;
using Shapers.Platform.Persistence;
using Shapers.Platform.Web;

namespace Shapers.IntegrationTests;

public sealed class StaffAccessTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Campus_staff_only_see_people_in_their_campus()
    {
        var admin = await api.SignInAdminAsync();
        var campuses = await (await admin.GetAsync("/api/admin/campuses")).ReadAsync<List<CampusDto>>();
        var rivonia = campuses.Single(c => c.Slug == "rivonia");
        var soweto = await (await admin.PostJsonAsync("/api/admin/campuses", new CreateCampusRequest("Soweto", "soweto-scope-test", null))).ReadAsync<CampusDto>();

        var inRivonia = await CreatePersonAsync(admin, "Rivonia", "Person", rivonia.Id);
        var inSoweto = await CreatePersonAsync(admin, "Soweto", "Person", soweto.Id);
        var staff = await CreateStaffAsync(admin, "Campus", "Admin", "campus.admin@test.local", rivonia.Id, SystemRoles.CampusAdministrator, rivonia.Scope);

        var list = await (await staff.GetAsync("/api/admin/people?pageSize=100")).ReadAsync<PagedResult<PersonListItemDto>>();
        Assert.Contains(list.Items, p => p.Id == inRivonia.Id);
        Assert.DoesNotContain(list.Items, p => p.Id == inSoweto.Id);

        // Out-of-scope records are indistinguishable from missing ones.
        Assert.Equal(HttpStatusCode.NotFound, (await staff.GetAsync($"/api/admin/people/{inSoweto.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync($"/api/admin/people/{inRivonia.Id}")).StatusCode);

        // Moving someone into a campus you don't manage is refused.
        var move = await staff.PostJsonAsync($"/api/admin/people/{inRivonia.Id}/campus", new MoveCampusRequest(soweto.Id));
        Assert.Equal(HttpStatusCode.Forbidden, move.StatusCode);
    }

    [Fact]
    public async Task Staff_cannot_hand_out_access_they_do_not_have()
    {
        var admin = await api.SignInAdminAsync();
        var rivonia = (await (await admin.GetAsync("/api/admin/campuses")).ReadAsync<List<CampusDto>>()).Single(c => c.Slug == "rivonia");
        var pastor = await CreateStaffAsync(admin, "Campus", "Pastor", "campus.pastor@test.local", rivonia.Id, SystemRoles.CampusPastor, rivonia.Scope);
        var colleague = await CreatePersonAsync(admin, "New", "Volunteer", rivonia.Id);
        await (await admin.PostJsonAsync($"/api/admin/people/{colleague.Id}/staff-login", new { email = "volunteer@test.local" })).ReadAsync<StaffLoginSetupDto>();
        var roles = await (await admin.GetAsync("/api/admin/roles")).ReadAsync<List<RoleDto>>();

        // A campus pastor can give a lesser role at their own campus...
        var allowed = await pastor.PostJsonAsync("/api/admin/grants", new CreateGrantRequest(colleague.Id, roles.Single(r => r.Name == SystemRoles.MinistryLeader).Id, rivonia.Scope, null, "Kids lead"));
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);

        // ...but not the church administrator role, and not church-wide.
        var escalation = await pastor.PostJsonAsync("/api/admin/grants", new CreateGrantRequest(colleague.Id, roles.Single(r => r.Name == SystemRoles.ChurchAdministrator).Id, rivonia.Scope, null, null));
        Assert.Equal(HttpStatusCode.Forbidden, escalation.StatusCode);
        Assert.Contains("identity.escalation", await escalation.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var wider = await pastor.PostJsonAsync("/api/admin/grants", new CreateGrantRequest(colleague.Id, roles.Single(r => r.Name == SystemRoles.MinistryLeader).Id, "shapers", null, null));
        Assert.Equal(HttpStatusCode.Forbidden, wider.StatusCode);
    }

    [Fact]
    public async Task Cookie_writes_without_the_csrf_header_are_rejected()
    {
        var client = api.Browser();
        (await client.PostAsJsonAsync("/api/auth/staff/login", new { email = ApiFactory.AdminEmail, password = ApiFactory.AdminPassword })).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync("/api/admin/people", new CreatePersonRequest("No", "Header", null, null, null, null, null, null, null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("csrf_header_missing", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Merging_moves_the_login_to_the_surviving_record()
    {
        var admin = await api.SignInAdminAsync();
        var rivonia = (await (await admin.GetAsync("/api/admin/campuses")).ReadAsync<List<CampusDto>>()).Single(c => c.Slug == "rivonia");
        var survivor = await CreatePersonAsync(admin, "Bongani", "Zulu", rivonia.Id);
        var duplicate = await CreatePersonAsync(admin, "Bongani", "Zulu", rivonia.Id);
        await (await admin.PostJsonAsync($"/api/admin/people/{duplicate.Id}/staff-login", new { email = "bongani@test.local" })).ReadAsync<StaffLoginSetupDto>();

        var merged = await (await admin.PostJsonAsync("/api/admin/people/merge", new MergeRequest(survivor.Id, duplicate.Id))).ReadAsync<PersonDetailDto>();
        Assert.Equal(survivor.Id, merged.Id);

        // The login follows via the outbox, asynchronously.
        PersonAccessDto access = null!;
        for (var attempt = 0; attempt < 30; attempt++)
        {
            access = await (await admin.GetAsync($"/api/admin/people/{survivor.Id}/access")).ReadAsync<PersonAccessDto>();
            if (access.UserId is not null)
            {
                break;
            }

            await Task.Delay(500);
        }

        Assert.NotNull(access.UserId);
        var tombstone = await (await admin.GetAsync($"/api/admin/people/{duplicate.Id}")).ReadAsync<PersonDetailDto>();
        Assert.Equal(survivor.Id, tombstone.MergedIntoId);
    }

    [Fact]
    public async Task Viewing_a_person_is_audited_and_the_audit_log_cannot_be_altered()
    {
        var admin = await api.SignInAdminAsync();
        var person = await CreatePersonAsync(admin, "Audit", "Subject", null);
        await admin.GetAsync($"/api/admin/people/{person.Id}");

        var entries = await (await admin.GetAsync($"/api/admin/audit?entityType=person&entityId={person.Id}")).ReadAsync<PagedResult<PlatformEndpoints.AuditEntryDto>>();
        Assert.Contains(entries.Items, e => e.Action == "people.person.viewed" && e.IsSensitiveRead);
        Assert.Contains(entries.Items, e => e.Action == "people.person.created");

        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM platform.audit_entries"));
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE platform.audit_entries SET action = 'x'"));
    }

    private static async Task<PersonDetailDto> CreatePersonAsync(HttpClient admin, string first, string last, Guid? campusId) =>
        await (await admin.PostJsonAsync("/api/admin/people", new CreatePersonRequest(first, last, null, null, null, campusId, null, null, null)))
            .ReadAsync<PersonDetailDto>();

    private async Task<HttpClient> CreateStaffAsync(HttpClient admin, string first, string last, string email, Guid campusId, string roleName, string scope)
    {
        var person = await CreatePersonAsync(admin, first, last, campusId);
        var setup = await (await admin.PostJsonAsync($"/api/admin/people/{person.Id}/staff-login", new { email })).ReadAsync<StaffLoginSetupDto>();
        const string password = "a-long-enough-password";
        (await admin.PostJsonAsync("/api/auth/staff/set-password", new { setup.UserId, token = setup.SetupToken, password })).EnsureSuccessStatusCode();

        var roles = await (await admin.GetAsync("/api/admin/roles")).ReadAsync<List<RoleDto>>();
        await (await admin.PostJsonAsync("/api/admin/grants", new CreateGrantRequest(person.Id, roles.Single(r => r.Name == roleName).Id, scope, null, "test"))).ReadAsync<GrantDto>();

        return await api.SignInStaffAsync(email, password);
    }
}
