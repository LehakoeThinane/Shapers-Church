using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shapers.Identity.Contracts;
using Shapers.Identity.Domain;
using Shapers.People.Contracts;
using Shapers.Platform.Text;

namespace Shapers.Identity.Application;

public sealed record RequestCodeRequest(string Phone);

public sealed record RequestCodeResponse(Guid ChallengeId, DateTimeOffset ExpiresAt, string MaskedPhone);

public sealed record VerifyCodeRequest(Guid ChallengeId, string Code, string? Device);

public sealed record TokenPair(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt);

public enum SignInStatus
{
    SignedIn,
    RegistrationRequired,
}

public sealed record SignInResponse(SignInStatus Status, TokenPair? Tokens, string? RegistrationTicket, bool ExistingRecordFound);

public sealed record RegisterRequest(
    Guid ChallengeId,
    string RegistrationTicket,
    string FirstName,
    string LastName,
    string? Email,
    Guid? CampusId,
    string PolicyVersion,
    IReadOnlyList<ConsentDecision> Consents,
    string? Device);

public sealed record RefreshRequest(string RefreshToken, string? Device);

/// <summary>
/// Member sign-in with a one-time code sent to their phone. Responses never reveal whether
/// a number already belongs to someone until the code has been proved.
/// </summary>
public sealed class OtpLoginService(
    IIdentityDb db,
    IUserAccounts accounts,
    IPeopleRegistration people,
    IOtpHasher hasher,
    ISmsSender sms,
    SessionService sessions,
    IOptions<IdentitySecurityOptions> options,
    TimeProvider clock)
{
    private static readonly Error InvalidCode = Error.Unauthorized("identity.code_invalid", "That code isn't right, or it has expired. Request a new one.");

    public async Task<Result<RequestCodeResponse>> RequestCodeAsync(RequestCodeRequest request, string? ip, CancellationToken cancellationToken)
    {
        if (!ContactNormaliser.TryNormalisePhone(request.Phone, out var phone))
        {
            return new Error("identity.phone_invalid", "Enter a valid mobile number, e.g. 082 123 4567.");
        }

        var now = clock.GetUtcNow();
        var recent = await db.OtpChallenges.AsNoTracking()
            .Where(c => c.Phone == phone && c.CreatedAt > now.AddDays(-1))
            .Select(c => c.CreatedAt)
            .ToListAsync(cancellationToken);
        if (recent.Count(t => t > now.AddMinutes(-15)) >= options.Value.MaxCodesPer15Minutes || recent.Count >= options.Value.MaxCodesPerDay)
        {
            return new Error("identity.too_many_codes", "Too many codes requested for this number. Please wait a while and try again.", ErrorKind.RateLimited);
        }

        var challengeId = Guid.CreateVersion7();
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var challenge = OtpChallenge.Create(challengeId, phone, hasher.HashCode(challengeId, code), now, ip);
        db.OtpChallenges.Add(challenge);
        await db.SaveChangesAsync(cancellationToken);

        await sms.SendAsync(phone, $"{code} is your Shapers Church code. It expires in {OtpChallenge.Lifetime.TotalMinutes:0} minutes. Never share it.", cancellationToken);
        return new RequestCodeResponse(challengeId, challenge.ExpiresAt, ContactNormaliser.MaskPhone(phone));
    }

    public async Task<Result<SignInResponse>> VerifyCodeAsync(VerifyCodeRequest request, CancellationToken cancellationToken)
    {
        var challenge = await db.OtpChallenges.SingleOrDefaultAsync(c => c.Id == request.ChallengeId, cancellationToken);
        if (challenge is null || string.IsNullOrWhiteSpace(request.Code) || request.Code.Length > 10)
        {
            return InvalidCode;
        }

        var now = clock.GetUtcNow();
        var outcome = challenge.Verify(hasher.HashCode(challenge.Id, request.Code.Trim()), now);
        if (outcome != OtpVerification.Success)
        {
            await db.SaveChangesAsync(cancellationToken);
            return outcome == OtpVerification.TooManyAttempts
                ? new Error("identity.code_locked", "Too many wrong codes. Request a new one.", ErrorKind.RateLimited)
                : InvalidCode;
        }

        var user = await accounts.FindByVerifiedPhoneAsync(challenge.Phone, cancellationToken);
        if (user is not null)
        {
            await db.SaveChangesAsync(cancellationToken);
            var tokens = await sessions.StartAsync(user, ["otp"], request.Device, cancellationToken);
            return new SignInResponse(SignInStatus.SignedIn, tokens, null, ExistingRecordFound: true);
        }

        var ticket = NewTicket();
        challenge.AttachRegistrationTicket(hasher.HashTicket(ticket));
        await db.SaveChangesAsync(cancellationToken);

        var linkable = await people.FindLinkablePersonAsync(challenge.Phone, cancellationToken);
        return new SignInResponse(SignInStatus.RegistrationRequired, null, ticket, ExistingRecordFound: linkable is not null);
    }

    public async Task<Result<SignInResponse>> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var challenge = await db.OtpChallenges.SingleOrDefaultAsync(c => c.Id == request.ChallengeId, cancellationToken);
        var now = clock.GetUtcNow();
        if (challenge is null || !challenge.CanRegister(hasher.HashTicket(request.RegistrationTicket ?? string.Empty), now))
        {
            return Error.Unauthorized("identity.ticket_invalid", "Your sign-up session has expired. Please verify your number again.");
        }

        if (await accounts.FindByVerifiedPhoneAsync(challenge.Phone, cancellationToken) is not null)
        {
            return Error.Conflict("identity.already_registered", "This number already has an account. Sign in instead.");
        }

        var outcome = await people.RegisterAsync(
            new SelfRegistration(request.FirstName, request.LastName, challenge.Phone, request.Email, request.CampusId, request.PolicyVersion, request.Consents),
            cancellationToken);

        var created = await accounts.CreateMemberAsync(outcome.PersonId, challenge.Phone, request.Email, cancellationToken);
        if (created.IsFailure)
        {
            return created.Error!;
        }

        challenge.CompleteRegistration(now);
        db.Publish(new UserRegisteredIntegrationEvent(created.Value.Id, outcome.PersonId));
        await db.SaveChangesAsync(cancellationToken);

        var tokens = await sessions.StartAsync(created.Value, ["otp"], request.Device, cancellationToken);
        return new SignInResponse(SignInStatus.SignedIn, tokens, null, outcome.LinkedExistingRecord);
    }

    private static string NewTicket() => Base64Url(RandomNumberGenerator.GetBytes(32));

    internal static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>Issues, rotates and revokes token sessions for the mobile app.</summary>
public sealed class SessionService(
    IIdentityDb db,
    IUserAccounts accounts,
    ITokenIssuer issuer,
    IOptions<IdentitySecurityOptions> options,
    TimeProvider clock)
{
    private static readonly Error SessionInvalid = Error.Unauthorized("identity.session_invalid", "Your session has ended. Please sign in again.");

    public async Task<TokenPair> StartAsync(UserAccount user, IReadOnlyCollection<string> methods, string? device, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var (raw, hash) = NewRefreshToken();
        var refresh = RefreshToken.Issue(user.Id, hash, string.Join(' ', methods), device, now, options.Value.RefreshTokenLifetime);
        db.RefreshTokens.Add(refresh);
        await db.SaveChangesAsync(cancellationToken);
        await accounts.RecordSignInAsync(user.Id, now, cancellationToken);

        var access = issuer.CreateAccessToken(user, methods, now);
        return new TokenPair(access.Token, access.ExpiresAt, raw, refresh.ExpiresAt);
    }

    public async Task<Result<TokenPair>> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var hash = HashRefreshToken(request.RefreshToken ?? string.Empty);
        var token = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (token is null)
        {
            return SessionInvalid;
        }

        if (token.RevokedAt is not null)
        {
            // A retired token came back: it was copied. End every session descended from the same sign-in.
            if (token.RevocationReason == "rotated")
            {
                var family = await db.RefreshTokens.Where(t => t.FamilyId == token.FamilyId && t.RevokedAt == null).ToListAsync(cancellationToken);
                family.ForEach(t => t.Revoke("reuse_detected", now));
                await db.SaveChangesAsync(cancellationToken);
            }

            return SessionInvalid;
        }

        if (!token.IsActiveAt(now))
        {
            return SessionInvalid;
        }

        var user = await accounts.FindByIdAsync(token.UserId, cancellationToken);
        if (user is null)
        {
            token.Revoke("user_missing", now);
            await db.SaveChangesAsync(cancellationToken);
            return SessionInvalid;
        }

        var (raw, newHash) = NewRefreshToken();
        var next = token.Rotate(newHash, now, options.Value.RefreshTokenLifetime);
        db.RefreshTokens.Add(next);
        await db.SaveChangesAsync(cancellationToken);

        var access = issuer.CreateAccessToken(user, token.AuthenticationMethods.Split(' ', StringSplitOptions.RemoveEmptyEntries), now);
        return new TokenPair(access.Token, access.ExpiresAt, raw, next.ExpiresAt);
    }

    public async Task RevokeAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var hash = HashRefreshToken(refreshToken);
        var token = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (token is not null)
        {
            token.Revoke("signed_out", clock.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>Refresh tokens are 256 random bits, so a plain SHA-256 is enough to protect them at rest.</summary>
    public static string HashRefreshToken(string raw) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw)));

    private static (string Raw, string Hash) NewRefreshToken()
    {
        var raw = OtpLoginService.Base64Url(RandomNumberGenerator.GetBytes(32));
        return (raw, HashRefreshToken(raw));
    }
}
