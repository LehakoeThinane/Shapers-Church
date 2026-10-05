namespace Shapers.Identity.Contracts;

public static class IdentityPermissions
{
    public const string UsersView = "identity.users.view";
    public const string RolesManage = "identity.roles.manage";
    public const string GrantsManage = "identity.grants.manage";
}

/// <summary>A new login was created and linked to a church record (the brief's "MemberRegistered").</summary>
public sealed record UserRegisteredIntegrationEvent(Guid UserId, Guid PersonId) : IntegrationEvent;

public interface IUserDirectory
{
    Task<Guid?> GetUserIdForPersonAsync(Guid personId, CancellationToken cancellationToken = default);

    /// <summary>Which of these people already have a login.</summary>
    Task<IReadOnlySet<Guid>> PeopleWithUsersAsync(IReadOnlyCollection<Guid> personIds, CancellationToken cancellationToken = default);

    /// <summary>People whose current grants give them <paramref name="permission"/> over <paramref name="scope"/>, e.g. to alert them.</summary>
    Task<IReadOnlyList<Guid>> PeopleWithPermissionAsync(string permission, string scope, CancellationToken cancellationToken = default);
}
