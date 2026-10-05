using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Shapers.Identity.Application;
using Shapers.Platform.Authorization;
using Shapers.Platform.Text;

namespace Shapers.Identity.Infrastructure;

public sealed class JwtOptions
{
    public const string SectionName = "Auth:Jwt";

    public string Issuer { get; set; } = "shapers-api";

    public string Audience { get; set; } = "shapers-app";

    /// <summary>Base64, at least 32 bytes. Supplied by user-secrets locally and by Key Vault in Azure.</summary>
    public string? SigningKey { get; set; }
}

public sealed class OtpOptions
{
    public const string SectionName = "Auth:Otp";

    /// <summary>Base64, at least 32 bytes. The secret half of the one-time-code hash.</summary>
    public string? HashKey { get; set; }
}

/// <summary>
/// Holds the key material. In Development a missing key is replaced by a random one for the life of the
/// process (tokens then stop working after a restart, and the app refreshes them). Elsewhere it is required.
/// </summary>
public sealed partial class IdentityKeys
{
    public IdentityKeys(IOptions<JwtOptions> jwt, IOptions<OtpOptions> otp, bool isDevelopment, ILogger<IdentityKeys> logger)
    {
        SigningKey = new SymmetricSecurityKey(Load(jwt.Value.SigningKey, "Auth:Jwt:SigningKey", isDevelopment, logger));
        OtpKey = Load(otp.Value.HashKey, "Auth:Otp:HashKey", isDevelopment, logger);
    }

    public SymmetricSecurityKey SigningKey { get; }

    public byte[] OtpKey { get; }

    private static byte[] Load(string? configured, string name, bool isDevelopment, ILogger logger)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var bytes = Convert.FromBase64String(configured);
            return bytes.Length >= 32 ? bytes : throw new InvalidOperationException($"{name} must be at least 32 bytes.");
        }

        if (!isDevelopment)
        {
            throw new InvalidOperationException($"{name} is not configured.");
        }

        LogEphemeralKey(logger, name);
        return RandomNumberGenerator.GetBytes(32);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Name} is not set; using a temporary key. Set it with dotnet user-secrets to keep sessions across restarts.")]
    private static partial void LogEphemeralKey(ILogger logger, string name);
}

internal sealed class JwtTokenIssuer(IdentityKeys keys, IOptions<JwtOptions> options, IOptions<IdentitySecurityOptions> security) : ITokenIssuer
{
    private readonly JwtSecurityTokenHandler _handler = new() { MapInboundClaims = false };

    public AccessToken CreateAccessToken(UserAccount user, IReadOnlyCollection<string> authenticationMethods, DateTimeOffset now)
    {
        var expires = now + security.Value.AccessTokenLifetime;
        var claims = new List<Claim>
        {
            new(ShapersClaims.Subject, user.Id.ToString()),
            new(ShapersClaims.PersonId, user.PersonId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
        };
        claims.AddRange(authenticationMethods.Select(m => new Claim(ShapersClaims.AuthenticationMethod, m)));

        var token = new JwtSecurityToken(
            options.Value.Issuer,
            options.Value.Audience,
            claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: new SigningCredentials(keys.SigningKey, SecurityAlgorithms.HmacSha256));
        return new AccessToken(_handler.WriteToken(token), expires);
    }
}

internal sealed class HmacOtpHasher(IdentityKeys keys) : IOtpHasher
{
    public byte[] HashCode(Guid challengeId, string code) =>
        HMACSHA256.HashData(keys.OtpKey, Encoding.UTF8.GetBytes($"{challengeId:N}:{code}"));

    public byte[] HashTicket(string ticket) =>
        HMACSHA256.HashData(keys.OtpKey, Encoding.UTF8.GetBytes($"ticket:{ticket}"));
}

/// <summary>
/// Development only: writes the message to the log instead of sending it. Registration fails at startup
/// outside Development until a real SMS/WhatsApp provider is chosen.
/// </summary>
internal sealed partial class LoggingSmsSender(ILogger<LoggingSmsSender> logger) : ISmsSender
{
    public Task SendAsync(string phoneE164, string message, CancellationToken cancellationToken)
    {
        LogSms(logger, ContactNormaliser.MaskPhone(phoneE164), message);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "DEV SMS to {Phone}: {Message}")]
    private static partial void LogSms(ILogger logger, string phone, string message);
}

/// <summary>
/// For a deployment that has no SMS provider yet: the API runs, staff sign in as usual, and members asking
/// for an SMS code are told it isn't available instead of the code going anywhere.
/// </summary>
internal sealed class DisabledSmsSender : ISmsSender
{
    public Task SendAsync(string phoneE164, string message, CancellationToken cancellationToken) =>
        throw new DomainRuleException("identity.sms_unavailable", "Signing in with a code by SMS isn't available yet. Please try again soon.");
}

/// <summary>Adds the person link to cookie sessions, matching the claims in mobile access tokens.</summary>
internal sealed class ShapersClaimsPrincipalFactory(UserManager<User> users, IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<User>(users, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(User user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(ShapersClaims.PersonId, user.PersonId.ToString()));
        return identity;
    }
}
