using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Shapers.Platform.Authorization;

public static class ShapersClaims
{
    public const string Subject = "sub";
    public const string PersonId = "person_id";

    /// <summary>Authentication methods reference (RFC 8176). Contains "mfa" after a second-factor sign-in.</summary>
    public const string AuthenticationMethod = "amr";
    public const string Mfa = "mfa";
}

public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    Guid? UserId { get; }

    Guid? PersonId { get; }

    /// <summary>True when this session was established with a second factor.</summary>
    bool HasMfa { get; }
}

internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid? UserId => ReadGuid(ShapersClaims.Subject) ?? ReadGuid(ClaimTypes.NameIdentifier);

    public Guid? PersonId => ReadGuid(ShapersClaims.PersonId);

    public bool HasMfa =>
        Principal?.FindAll(ShapersClaims.AuthenticationMethod).Any(c => c.Value == ShapersClaims.Mfa) == true ||
        Principal?.FindAll(ClaimTypes.AuthenticationMethod).Any(c => c.Value == ShapersClaims.Mfa) == true;

    private Guid? ReadGuid(string claimType) =>
        IsAuthenticated && Guid.TryParse(Principal!.FindFirstValue(claimType), out var id) ? id : null;
}
