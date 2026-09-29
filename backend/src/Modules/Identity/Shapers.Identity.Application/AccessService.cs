using Microsoft.EntityFrameworkCore;
using Shapers.Church.Contracts;
using Shapers.Identity.Contracts;
using Shapers.Identity.Domain;
using Shapers.People.Contracts;
using Shapers.Platform.Auditing;
using Shapers.Platform.Authorization;
using Shapers.Platform.Text;

namespace Shapers.Identity.Application;

public sealed record PermissionScopesDto(string Permission, IReadOnlyList<string> Scopes);

public sealed record MyAccessDto(
    Guid UserId,
    Guid PersonId,
    string DisplayName,
    bool HasMfa,
    bool TwoFactorEnabled,
    bool MfaRequiredForSensitive,
    string Palette,
    IReadOnlyList<PermissionScopesDto> Permissions);

public sealed record PermissionDto(string Key, string Module, string Description, bool IsSensitive);

public sealed record RoleDto(Guid Id, string Name, string? Description, bool IsSystem, IReadOnlyList<string> Permissions);

public sealed record SaveRoleRequest(string Name, string? Description, IReadOnlyList<string> Permissions);

public sealed record GrantDto(Guid Id, Guid RoleId, string RoleName, string Scope, DateTimeOffset GrantedAt, DateTimeOffset? ExpiresAt, string? Reason, bool IsActive);

public sealed record PersonAccessDto(Guid PersonId, Guid? UserId, string? Email, string? Phone, bool TwoFactorEnabled, bool HasPassword, DateTimeOffset? LastSignInAt, IReadOnlyList<GrantDto> Grants);

public sealed record CreateGrantRequest(Guid PersonId, Guid RoleId, string Scope, DateTimeOffset? ExpiresAt, string? Reason);

public sealed record StaffLoginRequest(string Email);

public sealed record StaffLoginSetupDto(Guid UserId, string Email, string SetupToken);

public sealed record PreferencesRequest(string Palette);

public sealed class AccessService(
    IIdentityDb db,
    IUserAccounts accounts,
    GrantLoader loader,
    IAuthorizer authorizer,
    ICurrentUser currentUser,
    PermissionCatalog catalog,
    IPeopleDirectory people,
    IChurchDirectory church,
    IAuditLog audit,
    Microsoft.Extensions.Options.IOptions<IdentitySecurityOptions> security,
    TimeProvider clock)
{
    public async Task<Result<MyAccessDto>> GetMyAccessAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId || await accounts.FindByIdAsync(userId, cancellationToken) is not { } user)
        {
            return Error.Unauthorized("identity.not_signed_in", "Not signed in.");
        }

        var person = await people.GetAsync(user.PersonId, cancellationToken);
        var evaluator = await loader.LoadAsync(userId, cancellationToken);

        // Report what will actually work in this session, so the UI doesn't offer screens that will 403.
        var permissions = new List<PermissionScopesDto>();
        foreach (var (permission, _) in evaluator.Summary())
        {
            var scopes = await authorizer.ScopesForAsync(permission, cancellationToken);
            if (scopes.Count > 0)
            {
                permissions.Add(new PermissionScopesDto(permission, scopes.Select(s => s.Value).ToList()));
            }
        }

        return new MyAccessDto(user.Id, user.PersonId, person?.DisplayName ?? "Member", currentUser.HasMfa, user.TwoFactorEnabled, security.Value.RequireMfaForSensitivePermissions, user.Palette, permissions);
    }

    public async Task<Result> SetPreferencesAsync(PreferencesRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Error.Unauthorized("identity.not_signed_in", "Not signed in.");
        }

        if (!Palettes.IsValid(request.Palette))
        {
            return new Error("identity.palette_invalid", "Palette must be auto, midnight or rose.");
        }

        await accounts.SetPaletteAsync(userId, request.Palette, cancellationToken);
        return Result.Success();
    }

    public IReadOnlyList<PermissionDto> ListPermissions() =>
        catalog.All.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new PermissionDto(p.Key, p.Module, p.Description, p.IsSensitive)).ToList();

    public async Task<IReadOnlyList<RoleDto>> ListRolesAsync(CancellationToken cancellationToken) =>
        (await db.Roles.AsNoTracking().OrderByDescending(r => r.IsSystem).ThenBy(r => r.Name).ToListAsync(cancellationToken))
            .Select(ToDto)
            .ToList();

    public async Task<Result<RoleDto>> CreateRoleAsync(SaveRoleRequest request, CancellationToken cancellationToken)
    {
        var check = await CheckRoleRequestAsync(request, cancellationToken);
        if (check.IsFailure)
        {
            return check.Error!;
        }

        var role = Role.Create(request.Name, request.Description, request.Permissions);
        db.Roles.Add(role);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord("identity.role.created", "role", role.Id.ToString(), Details: new { role.Name, request.Permissions }), cancellationToken);
        return ToDto(role);
    }

    public async Task<Result<RoleDto>> UpdateRoleAsync(Guid id, SaveRoleRequest request, CancellationToken cancellationToken)
    {
        var check = await CheckRoleRequestAsync(request, cancellationToken);
        if (check.IsFailure)
        {
            return check.Error!;
        }

        var role = await db.Roles.SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (role is null)
        {
            return Error.NotFound("identity.role_not_found", "Role not found.");
        }

        role.Update(request.Name, request.Description, request.Permissions);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord("identity.role.updated", "role", role.Id.ToString(), Details: new { role.Name, request.Permissions }), cancellationToken);
        return ToDto(role);
    }

    public async Task<Result<PersonAccessDto>> GetPersonAccessAsync(Guid personId, CancellationToken cancellationToken)
    {
        var person = await people.GetAsync(personId, cancellationToken);
        if (person is null || !await authorizer.CanAsync(IdentityPermissions.UsersView, ScopePath.Parse(person.Scope), cancellationToken))
        {
            return Error.NotFound("identity.person_not_found", "Person not found.");
        }

        var user = await accounts.FindByPersonAsync(person.Id, cancellationToken);
        if (user is null)
        {
            return new PersonAccessDto(person.Id, null, null, null, false, false, null, []);
        }

        var now = clock.GetUtcNow();
        var grants = await db.Grants.AsNoTracking().Where(g => g.UserId == user.Id).OrderByDescending(g => g.GrantedAt).ToListAsync(cancellationToken);
        var roles = await db.Roles.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Name, cancellationToken);
        return new PersonAccessDto(
            person.Id,
            user.Id,
            user.Email,
            user.Phone is null ? null : ContactNormaliser.MaskPhone(user.Phone),
            user.TwoFactorEnabled,
            user.HasPassword,
            user.LastSignInAt,
            grants.Select(g => new GrantDto(g.Id, g.RoleId, roles.GetValueOrDefault(g.RoleId, "Unknown"), g.Scope, g.GrantedAt, g.ExpiresAt, g.Reason, g.IsActiveAt(now))).ToList());
    }

    public async Task<Result<GrantDto>> CreateGrantAsync(CreateGrantRequest request, CancellationToken cancellationToken)
    {
        if (!ScopePath.TryParse(request.Scope, out var scope) || !await church.ScopeExistsAsync(scope.Value, cancellationToken))
        {
            return new Error("identity.scope_invalid", "Choose a campus or ministry that exists.");
        }

        var role = await db.Roles.AsNoTracking().SingleOrDefaultAsync(r => r.Id == request.RoleId, cancellationToken);
        if (role is null)
        {
            return Error.NotFound("identity.role_not_found", "Role not found.");
        }

        var person = await people.GetAsync(request.PersonId, cancellationToken);
        var user = person is null ? null : await accounts.FindByPersonAsync(person.Id, cancellationToken);
        if (user is null)
        {
            return new Error("identity.no_login", "This person doesn't have a login yet.");
        }

        var grantor = await GrantorAsync(cancellationToken);
        var allowed = GrantPolicy.CanGrant(grantor, role.PermissionKeys, scope);
        if (allowed.IsFailure)
        {
            return allowed.Error!;
        }

        var grant = Grant.Create(user.Id, role.Id, scope, currentUser.UserId, clock.GetUtcNow(), request.ExpiresAt, request.Reason);
        db.Grants.Add(grant);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(
            new AuditRecord("identity.grant.created", "grant", grant.Id.ToString(), scope, new { user.PersonId, userId = user.Id, role = role.Name, request.ExpiresAt, request.Reason }),
            cancellationToken);
        return new GrantDto(grant.Id, role.Id, role.Name, grant.Scope, grant.GrantedAt, grant.ExpiresAt, grant.Reason, true);
    }

    public async Task<Result> RevokeGrantAsync(Guid grantId, CancellationToken cancellationToken)
    {
        var grant = await db.Grants.SingleOrDefaultAsync(g => g.Id == grantId, cancellationToken);
        if (grant is null)
        {
            return Error.NotFound("identity.grant_not_found", "Access grant not found.");
        }

        var allowed = GrantPolicy.CanRevoke(await GrantorAsync(cancellationToken), ScopePath.Parse(grant.Scope));
        if (allowed.IsFailure)
        {
            return allowed;
        }

        grant.Revoke(currentUser.UserId, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord("identity.grant.revoked", "grant", grant.Id.ToString(), ScopePath.Parse(grant.Scope), new { grant.UserId }), cancellationToken);
        return Result.Success();
    }

    public async Task<Result<StaffLoginSetupDto>> PrepareStaffLoginAsync(Guid personId, StaffLoginRequest request, CancellationToken cancellationToken)
    {
        var person = await people.GetAsync(personId, cancellationToken);
        if (person is null || !await authorizer.CanAsync(IdentityPermissions.GrantsManage, ScopePath.Parse(person.Scope), cancellationToken))
        {
            return Error.NotFound("identity.person_not_found", "Person not found.");
        }

        if (!ContactNormaliser.TryNormaliseEmail(request.Email, out var email))
        {
            return new Error("identity.email_invalid", "Enter a valid email address.");
        }

        var prepared = await accounts.PrepareStaffLoginAsync(person.Id, email, cancellationToken);
        if (prepared.IsFailure)
        {
            return prepared.Error!;
        }

        var (account, token) = prepared.Value;
        await audit.RecordAsync(new AuditRecord("identity.staff_login.prepared", "user", account.Id.ToString(), ScopePath.Parse(person.Scope), new { person.Id }), cancellationToken);
        return new StaffLoginSetupDto(account.Id, email, token);
    }

    private async Task<GrantEvaluator> GrantorAsync(CancellationToken cancellationToken)
    {
        // Grants-management is sensitive: without a second factor this session may not hand out access at all.
        if (currentUser.UserId is not { } userId || !await authorizer.HasAnywhereAsync(IdentityPermissions.GrantsManage, cancellationToken))
        {
            return new GrantEvaluator([]);
        }

        return await loader.LoadAsync(userId, cancellationToken);
    }

    private async Task<Result> CheckRoleRequestAsync(SaveRoleRequest request, CancellationToken cancellationToken)
    {
        var root = ScopePath.Parse((await church.GetRootScopeAsync(cancellationToken)).Path);
        if (!await authorizer.CanAsync(IdentityPermissions.RolesManage, root, cancellationToken))
        {
            return Error.Forbidden("identity.forbidden", "Only church-wide administrators can edit roles.");
        }

        var unknown = request.Permissions.Where(p => !catalog.Exists(p)).ToList();
        return unknown.Count == 0
            ? Result.Success()
            : new Error("identity.permission_unknown", $"Unknown permissions: {string.Join(", ", unknown)}.");
    }

    private static RoleDto ToDto(Role r) =>
        new(r.Id, r.Name, r.Description, r.IsSystem, r.PermissionKeys.Order(StringComparer.Ordinal).ToList());
}
