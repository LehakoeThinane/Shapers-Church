using Microsoft.EntityFrameworkCore;
using Shapers.Identity.Contracts;
using Shapers.Identity.Domain;
using Shapers.Platform.Authorization;

namespace Shapers.Identity.Application;

public interface IIdentityDb
{
    DbSet<Role> Roles { get; }

    DbSet<Grant> Grants { get; }

    DbSet<RefreshToken> RefreshTokens { get; }

    DbSet<OtpChallenge> OtpChallenges { get; }

    /// <summary>Queues an integration event in the outbox; it is written with the next save.</summary>
    void Publish(IIntegrationEvent integrationEvent);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public sealed record UserAccount(
    Guid Id,
    Guid PersonId,
    string? Email,
    string? Phone,
    bool TwoFactorEnabled,
    bool HasPassword,
    string Palette,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastSignInAt);

/// <summary>Login accounts. Wraps ASP.NET Core Identity so use cases don't depend on it directly.</summary>
public interface IUserAccounts
{
    Task<UserAccount?> FindByIdAsync(Guid userId, CancellationToken cancellationToken);

    Task<UserAccount?> FindByVerifiedPhoneAsync(string phone, CancellationToken cancellationToken);

    Task<UserAccount?> FindByPersonAsync(Guid personId, CancellationToken cancellationToken);

    Task<Result<UserAccount>> CreateMemberAsync(Guid personId, string verifiedPhone, string? email, CancellationToken cancellationToken);

    /// <summary>Creates (or reuses) a login with an email for a staff member, and returns a password-setup token.</summary>
    Task<Result<(UserAccount Account, string SetupToken)>> PrepareStaffLoginAsync(Guid personId, string email, CancellationToken cancellationToken);

    Task RecordSignInAsync(Guid userId, DateTimeOffset at, CancellationToken cancellationToken);

    Task SetPaletteAsync(Guid userId, string palette, CancellationToken cancellationToken);

    Task RelinkPersonAsync(Guid userId, Guid personId, CancellationToken cancellationToken);
}

/// <summary>How each built-in role ships, so a role the church changed can be put back.</summary>
public interface ISystemRoleDefaults
{
    (string Description, IReadOnlyList<string> Permissions)? For(string roleName);
}

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

public interface ITokenIssuer
{
    AccessToken CreateAccessToken(UserAccount user, IReadOnlyCollection<string> authenticationMethods, DateTimeOffset now);
}

/// <summary>Keyed hashing for one-time codes; a database leak alone doesn't reveal or allow guessing them.</summary>
public interface IOtpHasher
{
    byte[] HashCode(Guid challengeId, string code);

    byte[] HashTicket(string ticket);
}

public interface ISmsSender
{
    Task SendAsync(string phoneE164, string message, CancellationToken cancellationToken);
}

public sealed class IdentitySecurityOptions
{
    public const string SectionName = "Auth:Security";

    /// <summary>Sensitive permissions only work in sessions signed in with a second factor.</summary>
    public bool RequireMfaForSensitivePermissions { get; set; } = true;

    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(60);

    public int MaxCodesPer15Minutes { get; set; } = 3;

    public int MaxCodesPerDay { get; set; } = 10;
}

public sealed class IdentityPermissionProvider : IPermissionProvider
{
    public IEnumerable<PermissionDefinition> GetPermissions() =>
    [
        new(IdentityPermissions.UsersView, "identity", "See who has a login and what access they have"),
        new(IdentityPermissions.RolesManage, "identity", "Create roles and change what any role allows", IsSensitive: true),
        new(IdentityPermissions.GrantsManage, "identity", "Give and remove access (never beyond your own)", IsSensitive: true),
    ];
}

public static class Palettes
{
    public const string Auto = "auto";
    public const string Midnight = "midnight";
    public const string Rose = "rose";

    public static bool IsValid(string value) => value is Auto or Midnight or Rose;
}
