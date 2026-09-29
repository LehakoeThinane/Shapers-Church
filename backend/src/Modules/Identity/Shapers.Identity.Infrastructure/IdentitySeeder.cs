using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shapers.Church.Contracts;
using Shapers.Identity.Contracts;
using Shapers.Identity.Domain;
using Shapers.Events.Contracts;
using Shapers.Media.Contracts;
using Shapers.People.Contracts;
using Shapers.Platform.Authorization;
using Shapers.Platform.Messaging;

namespace Shapers.Identity.Infrastructure;

public static class SystemRoles
{
    public const string ChurchAdministrator = "Church administrator";
    public const string CampusPastor = "Campus pastor";
    public const string CampusAdministrator = "Campus administrator";
    public const string MinistryLeader = "Ministry leader";
    public const string MediaTeam = "Media team";
    public const string EventsTeam = "Events team";
    public const string DoorVolunteer = "Door volunteer";

    /// <summary>Built-in roles. The church administrator always holds every permission in the catalogue.</summary>
    public static IReadOnlyDictionary<string, (string Description, IReadOnlyList<string> Permissions)> Definitions(PermissionCatalog catalog) =>
        new Dictionary<string, (string, IReadOnlyList<string>)>
        {
            [ChurchAdministrator] = ("Full access. Give sparingly.", catalog.All.Select(p => p.Key).ToList()),
            [CampusPastor] = ("Pastoral oversight of a campus, including managing its team's access.",
            [
                PeoplePermissions.ProfilesView, PeoplePermissions.ProfilesEdit, PeoplePermissions.ProfilesMerge,
                ChurchPermissions.MinistriesManage, IdentityPermissions.UsersView, IdentityPermissions.GrantsManage,
            ]),
            [CampusAdministrator] = ("Day-to-day administration of a campus.",
            [
                PeoplePermissions.ProfilesView, PeoplePermissions.ProfilesEdit, ChurchPermissions.MinistriesManage,
            ]),
            [MinistryLeader] = ("Leads a ministry; can see the people in it.", [PeoplePermissions.ProfilesView]),
            [EventsTeam] = ("Plans events, manages registrations and runs the door.",
            [
                EventsPermissions.Edit, EventsPermissions.Publish, EventsPermissions.RegistrationsView, EventsPermissions.RegistrationsManage, EventsPermissions.CheckIn,
            ]),
            [DoorVolunteer] = ("Checks people in at events. Sees names only.", [EventsPermissions.CheckIn]),
            [MediaTeam] = ("Prepares and publishes sermons, series and speakers.",
            [
                MediaPermissions.SermonsEdit, MediaPermissions.SermonsPublish, MediaPermissions.SpeakersManage, MediaPermissions.LivestreamManage,
            ]),
        };
}

public sealed class BootstrapAdminOptions
{
    public const string SectionName = "Auth:BootstrapAdmin";

    public string? Email { get; set; }

    public string FirstName { get; set; } = "Church";

    public string LastName { get; set; } = "Administrator";

    /// <summary>Required outside Development. In Development a random password is generated and logged once.</summary>
    public string? Password { get; set; }
}

internal sealed partial class IdentitySeeder(
    IdentityDbContext db,
    UserManager<User> users,
    PermissionCatalog catalog,
    IPeopleRegistration people,
    IChurchDirectory church,
    TimeProvider clock,
    ILogger<IdentitySeeder> logger)
{
    public async Task SeedRolesAsync(CancellationToken cancellationToken)
    {
        var existing = await db.Roles.Where(r => r.IsSystem).ToDictionaryAsync(r => r.Name, cancellationToken);
        foreach (var (name, (description, permissions)) in SystemRoles.Definitions(catalog))
        {
            if (existing.TryGetValue(name, out var role))
            {
                role.SyncSystemPermissions(permissions);
            }
            else
            {
                db.Roles.Add(Role.Create(name, description, permissions, isSystem: true));
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Creates the first church administrator when nobody holds that role yet, so a new deployment
    /// is never locked out. Does nothing once an administrator exists.
    /// </summary>
    public async Task SeedBootstrapAdminAsync(BootstrapAdminOptions options, bool isDevelopment, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Email))
        {
            return;
        }

        var adminRole = await db.Roles.SingleAsync(r => r.IsSystem && r.Name == SystemRoles.ChurchAdministrator, cancellationToken);
        var root = ScopePath.Parse((await church.GetRootScopeAsync(cancellationToken)).Path);
        var now = clock.GetUtcNow();
        var hasAdmin = await db.Grants.AnyAsync(
            g => g.RoleId == adminRole.Id && g.Scope == root.Value && g.RevokedAt == null && (g.ExpiresAt == null || g.ExpiresAt > now),
            cancellationToken);
        if (hasAdmin)
        {
            return;
        }

        var password = options.Password;
        if (string.IsNullOrWhiteSpace(password))
        {
            if (!isDevelopment)
            {
                throw new InvalidOperationException("Auth:BootstrapAdmin:Password must be set to create the first administrator.");
            }

            password = GeneratePassword();
        }

        var personId = await people.EnsureStaffRecordAsync(options.FirstName, options.LastName, options.Email, cancellationToken);
        var user = await users.FindByEmailAsync(options.Email);
        if (user is null)
        {
            var id = Guid.CreateVersion7();
            user = new User
            {
                Id = id,
                UserName = id.ToString("N"),
                Email = options.Email,
                EmailConfirmed = true,
                PersonId = personId,
                CreatedAt = now,
            };
            Check(await users.CreateAsync(user, password));
            db.Publish(new UserRegisteredIntegrationEvent(user.Id, personId));
        }
        else
        {
            Check(await users.RemovePasswordAsync(user));
            Check(await users.AddPasswordAsync(user, password));
        }

        db.Grants.Add(Grant.Create(user.Id, adminRole.Id, root, grantedBy: null, now, expiresAt: null, "Bootstrap administrator"));
        await db.SaveChangesAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(options.Password))
        {
            LogGeneratedPassword(logger, options.Email, password);
        }
    }

    private static string GeneratePassword() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(18)).Replace('+', 'x').Replace('/', 'y');

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join(" ", result.Errors.Select(e => e.Description)));
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "DEV bootstrap administrator created: {Email} / {Password}  (development only; change it after signing in)")]
    private static partial void LogGeneratedPassword(ILogger logger, string email, string password);
}

/// <summary>When two church records merge, a login on the duplicate follows the survivor.</summary>
internal sealed class RelinkUserOnPeopleMerged(IdentityDbContext db) : IIntegrationEventHandler<PeopleMergedIntegrationEvent>
{
    public async Task HandleAsync(PeopleMergedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var survivorHasLogin = await db.Users.AnyAsync(u => u.PersonId == integrationEvent.SurvivorId, cancellationToken);
        if (survivorHasLogin)
        {
            return;
        }

        await db.Users
            .Where(u => u.PersonId == integrationEvent.MergedId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.PersonId, integrationEvent.SurvivorId), cancellationToken);
    }
}
