using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shapers.Identity.Domain;
using Shapers.Platform.Authorization;

namespace Shapers.Identity.Application;

public sealed class GrantLoader(IIdentityDb db, TimeProvider clock)
{
    public async Task<GrantEvaluator> LoadAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var grants = await db.Grants.AsNoTracking()
            .Where(g => g.UserId == userId && g.RevokedAt == null && (g.ExpiresAt == null || g.ExpiresAt > now))
            .Select(g => new { g.RoleId, g.Scope })
            .ToListAsync(cancellationToken);

        var roleIds = grants.Select(g => g.RoleId).Distinct().ToList();
        var roles = await db.Roles.AsNoTracking()
            .Where(r => roleIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.PermissionKeys, cancellationToken);

        return new GrantEvaluator(grants
            .Where(g => roles.ContainsKey(g.RoleId))
            .Select(g => new EffectiveGrant(ScopePath.Parse(g.Scope), roles[g.RoleId]))
            .ToList());
    }
}

/// <summary>
/// The platform's <see cref="IAuthorizer"/>. Loads the signed-in user's grants once per request.
/// Sensitive permissions are withheld from sessions without a second factor.
/// </summary>
public sealed class ScopedAuthorizer(
    GrantLoader loader,
    ICurrentUser currentUser,
    PermissionCatalog catalog,
    IOptions<IdentitySecurityOptions> options) : IAuthorizer
{
    private GrantEvaluator? _evaluator;

    public async Task<bool> CanAsync(string permission, ScopePath scope, CancellationToken cancellationToken = default) =>
        Allowed(permission) && (await EvaluatorAsync(cancellationToken)).Can(permission, scope);

    public async Task<bool> HasAnywhereAsync(string permission, CancellationToken cancellationToken = default) =>
        Allowed(permission) && (await EvaluatorAsync(cancellationToken)).HasAnywhere(permission);

    public async Task<IReadOnlyList<ScopePath>> ScopesForAsync(string permission, CancellationToken cancellationToken = default) =>
        Allowed(permission) ? (await EvaluatorAsync(cancellationToken)).ScopesFor(permission) : [];

    private bool Allowed(string permission) =>
        currentUser.IsAuthenticated
        && (!options.Value.RequireMfaForSensitivePermissions || !catalog.IsSensitive(permission) || currentUser.HasMfa);

    private async Task<GrantEvaluator> EvaluatorAsync(CancellationToken cancellationToken) =>
        _evaluator ??= currentUser.UserId is { } userId
            ? await loader.LoadAsync(userId, cancellationToken)
            : new GrantEvaluator([]);
}
