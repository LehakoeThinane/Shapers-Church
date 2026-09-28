namespace Shapers.Identity.Domain;

/// <summary>A named bundle of permissions. Roles say *what*; grants say *where*.</summary>
public sealed class Role : AggregateRoot<Guid>
{
    private readonly List<RolePermission> _permissions = [];

    private Role()
    {
    }

    public string Name { get; private set; } = null!;

    public string? Description { get; private set; }

    /// <summary>System roles are defined in code and kept in sync at startup; staff cannot edit them.</summary>
    public bool IsSystem { get; private set; }

    public IReadOnlyList<RolePermission> Permissions => _permissions;

    public IReadOnlySet<string> PermissionKeys => _permissions.Select(p => p.PermissionKey).ToHashSet(StringComparer.Ordinal);

    public static Role Create(string name, string? description, IEnumerable<string> permissions, bool isSystem = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var role = new Role
        {
            Id = Guid.CreateVersion7(),
            Name = name.Trim(),
            Description = description?.Trim(),
            IsSystem = isSystem,
        };
        role.ReplacePermissions(permissions);
        return role;
    }

    public void Update(string name, string? description, IEnumerable<string> permissions)
    {
        if (IsSystem)
        {
            throw new DomainRuleException("identity.system_role", "Built-in roles can't be edited. Create a custom role instead.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        Description = description?.Trim();
        ReplacePermissions(permissions);
    }

    /// <summary>Startup sync for built-in roles.</summary>
    public void SyncSystemPermissions(IEnumerable<string> permissions)
    {
        if (!IsSystem)
        {
            throw new InvalidOperationException("Only system roles are synced from code.");
        }

        ReplacePermissions(permissions);
    }

    private void ReplacePermissions(IEnumerable<string> permissions)
    {
        var wanted = permissions.ToHashSet(StringComparer.Ordinal);
        _permissions.RemoveAll(p => !wanted.Contains(p.PermissionKey));
        foreach (var key in wanted.Where(k => _permissions.All(p => p.PermissionKey != k)))
        {
            _permissions.Add(new RolePermission(key));
        }
    }
}

public sealed class RolePermission
{
    private RolePermission()
    {
    }

    internal RolePermission(string permissionKey) => PermissionKey = permissionKey;

    public string PermissionKey { get; private set; } = null!;
}

/// <summary>Gives a user a role at a scope: "Thandi is Campus pastor at Rivonia".</summary>
public sealed class Grant : AggregateRoot<Guid>
{
    private Grant()
    {
    }

    public Guid UserId { get; private set; }

    public Guid RoleId { get; private set; }

    public string Scope { get; private set; } = null!;

    public Guid? GrantedByUserId { get; private set; }

    public DateTimeOffset GrantedAt { get; private set; }

    public DateTimeOffset? ExpiresAt { get; private set; }

    public string? Reason { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public Guid? RevokedByUserId { get; private set; }

    public static Grant Create(Guid userId, Guid roleId, ScopePath scope, Guid? grantedBy, DateTimeOffset now, DateTimeOffset? expiresAt, string? reason)
    {
        if (expiresAt is { } expiry && expiry <= now)
        {
            throw new DomainRuleException("identity.grant_expired", "A grant must expire in the future.");
        }

        return new Grant
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            RoleId = roleId,
            Scope = scope.Value,
            GrantedByUserId = grantedBy,
            GrantedAt = now,
            ExpiresAt = expiresAt,
            Reason = reason?.Trim(),
        };
    }

    public bool IsActiveAt(DateTimeOffset now) => RevokedAt is null && (ExpiresAt is null || ExpiresAt > now);

    public void Revoke(Guid? by, DateTimeOffset now)
    {
        if (RevokedAt is not null)
        {
            return;
        }

        RevokedAt = now;
        RevokedByUserId = by;
    }
}

/// <summary>What a grant means once its role is resolved: a set of permissions at one scope.</summary>
public sealed record EffectiveGrant(ScopePath Scope, IReadOnlySet<string> Permissions);

/// <summary>
/// Pure permission logic. A grant covers a scope when the grant's path is that scope or an ancestor of it;
/// permissions never flow upwards (a ministry grant says nothing about its campus).
/// </summary>
public sealed class GrantEvaluator(IReadOnlyList<EffectiveGrant> grants)
{
    public IReadOnlyList<EffectiveGrant> Grants => grants;

    public bool Can(string permission, ScopePath scope) =>
        grants.Any(g => g.Permissions.Contains(permission) && g.Scope.Covers(scope));

    public bool HasAnywhere(string permission) => grants.Any(g => g.Permissions.Contains(permission));

    public IReadOnlyList<ScopePath> ScopesFor(string permission)
    {
        var scopes = grants.Where(g => g.Permissions.Contains(permission)).Select(g => g.Scope).Distinct().OrderBy(s => s.Depth).ToList();
        var result = new List<ScopePath>();
        foreach (var scope in scopes.Where(scope => !result.Any(r => r.Covers(scope))))
        {
            result.Add(scope);
        }

        return result;
    }

    /// <summary>All permissions the user holds anywhere, each with the scopes where it applies.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<ScopePath>> Summary() =>
        grants.SelectMany(g => g.Permissions).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .ToDictionary(p => p, ScopesFor);
}

/// <summary>
/// Rules for handing out access. Nobody can grant more than they hold: the grantor needs the
/// grants-management permission at the target scope, and every permission in the role at that scope too.
/// </summary>
public static class GrantPolicy
{
    public const string ManageGrantsPermission = "identity.grants.manage";

    public static Result CanGrant(GrantEvaluator grantor, IReadOnlySet<string> rolePermissions, ScopePath targetScope)
    {
        if (!grantor.Can(ManageGrantsPermission, targetScope))
        {
            return Error.Forbidden("identity.cannot_manage_grants", "You can't manage access at this scope.");
        }

        var missing = rolePermissions.Where(p => !grantor.Can(p, targetScope)).Order(StringComparer.Ordinal).ToList();
        return missing.Count == 0
            ? Result.Success()
            : Error.Forbidden(
                "identity.escalation",
                $"You can't grant permissions you don't hold here: {string.Join(", ", missing)}.");
    }

    public static Result CanRevoke(GrantEvaluator revoker, ScopePath grantScope) =>
        revoker.Can(ManageGrantsPermission, grantScope)
            ? Result.Success()
            : Error.Forbidden("identity.cannot_manage_grants", "You can't manage access at this scope.");
}
