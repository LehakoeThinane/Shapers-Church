namespace Shapers.Platform.Authorization;

/// <summary>
/// A permission a role can include, named <c>module.resource.action</c>.
/// Sensitive permissions touch special personal information: using one is audited and needs a second factor.
/// </summary>
public sealed record PermissionDefinition(string Key, string Module, string Description, bool IsSensitive = false);

/// <summary>Each module declares its permissions in code; they are synced to the database at startup.</summary>
public interface IPermissionProvider
{
    IEnumerable<PermissionDefinition> GetPermissions();
}

public sealed class PermissionCatalog
{
    private readonly Dictionary<string, PermissionDefinition> _permissions;

    public PermissionCatalog(IEnumerable<IPermissionProvider> providers)
    {
        _permissions = [];
        foreach (var permission in providers.SelectMany(p => p.GetPermissions()))
        {
            if (!_permissions.TryAdd(permission.Key, permission))
            {
                throw new InvalidOperationException($"Permission '{permission.Key}' is declared twice.");
            }
        }
    }

    public IReadOnlyCollection<PermissionDefinition> All => _permissions.Values;

    public bool Exists(string key) => _permissions.ContainsKey(key);

    public bool IsSensitive(string key) => _permissions.TryGetValue(key, out var p) && p.IsSensitive;
}

public static class PlatformPermissions
{
    public const string AuditView = "platform.audit.view";
    public const string JobsView = "platform.jobs.view";

    public sealed class Provider : IPermissionProvider
    {
        public IEnumerable<PermissionDefinition> GetPermissions() =>
        [
            new(AuditView, "platform", "View the audit log", IsSensitive: true),
            new(JobsView, "platform", "View and retry background jobs"),
        ];
    }
}
