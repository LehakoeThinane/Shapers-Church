using System.Security.Cryptography;

namespace Shapers.Identity.Domain;

/// <summary>
/// A long-lived credential that buys a new access token. Only a hash is stored. Every use rotates it:
/// the old token is retired and a new one issued in the same family. If a retired token is ever presented
/// again, someone has a copy, so the whole family is revoked and the device must sign in again.
/// </summary>
public sealed class RefreshToken
{
    private RefreshToken()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public Guid FamilyId { get; private set; }

    public string TokenHash { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public string? RevocationReason { get; private set; }

    public Guid? ReplacedById { get; private set; }

    /// <summary>How the session was established, e.g. "otp" or "pwd mfa". Carried across rotations.</summary>
    public string AuthenticationMethods { get; private set; } = null!;

    public string? Device { get; private set; }

    public static RefreshToken Issue(Guid userId, string tokenHash, string authenticationMethods, string? device, DateTimeOffset now, TimeSpan lifetime, Guid? familyId = null) => new()
    {
        Id = Guid.CreateVersion7(),
        UserId = userId,
        FamilyId = familyId ?? Guid.CreateVersion7(),
        TokenHash = tokenHash,
        CreatedAt = now,
        ExpiresAt = now + lifetime,
        AuthenticationMethods = authenticationMethods,
        Device = device is { Length: > 200 } ? device[..200] : device,
    };

    public bool IsActiveAt(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    public RefreshToken Rotate(string newTokenHash, DateTimeOffset now, TimeSpan lifetime)
    {
        if (!IsActiveAt(now))
        {
            throw new InvalidOperationException("Only an active refresh token can be rotated.");
        }

        var next = Issue(UserId, newTokenHash, AuthenticationMethods, Device, now, lifetime, FamilyId);
        RevokedAt = now;
        RevocationReason = "rotated";
        ReplacedById = next.Id;
        return next;
    }

    public void Revoke(string reason, DateTimeOffset now)
    {
        if (RevokedAt is not null)
        {
            return;
        }

        RevokedAt = now;
        RevocationReason = reason;
    }
}

public enum OtpVerification
{
    Success,
    Invalid,
    Expired,
    TooManyAttempts,
    AlreadyUsed,
}

/// <summary>
/// A one-time code sent to a phone. Codes are stored as keyed hashes, expire quickly and allow a few tries.
/// After a successful check the challenge can issue one registration ticket for a first-time member.
/// </summary>
public sealed class OtpChallenge
{
    public const int MaxAttempts = 5;
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan RegistrationWindow = TimeSpan.FromMinutes(20);

    private OtpChallenge()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>E.164.</summary>
    public string Phone { get; private set; } = null!;

    public byte[] CodeHash { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset? VerifiedAt { get; private set; }

    public byte[]? RegistrationTicketHash { get; private set; }

    public DateTimeOffset? RegistrationCompletedAt { get; private set; }

    public string? RequestedFromIp { get; private set; }

    public static OtpChallenge Create(Guid id, string phone, byte[] codeHash, DateTimeOffset now, string? ip) => new()
    {
        Id = id,
        Phone = phone,
        CodeHash = codeHash,
        CreatedAt = now,
        ExpiresAt = now + Lifetime,
        RequestedFromIp = ip,
    };

    public OtpVerification Verify(byte[] candidateHash, DateTimeOffset now)
    {
        if (VerifiedAt is not null)
        {
            return OtpVerification.AlreadyUsed;
        }

        if (now >= ExpiresAt)
        {
            return OtpVerification.Expired;
        }

        if (Attempts >= MaxAttempts)
        {
            return OtpVerification.TooManyAttempts;
        }

        Attempts++;
        if (!CryptographicOperations.FixedTimeEquals(candidateHash, CodeHash))
        {
            return Attempts >= MaxAttempts ? OtpVerification.TooManyAttempts : OtpVerification.Invalid;
        }

        VerifiedAt = now;
        return OtpVerification.Success;
    }

    public void AttachRegistrationTicket(byte[] ticketHash)
    {
        if (VerifiedAt is null)
        {
            throw new InvalidOperationException("Verify the code before issuing a registration ticket.");
        }

        RegistrationTicketHash = ticketHash;
    }

    public bool CanRegister(byte[] ticketHash, DateTimeOffset now) =>
        VerifiedAt is { } verified
        && RegistrationCompletedAt is null
        && RegistrationTicketHash is not null
        && now < verified + RegistrationWindow
        && CryptographicOperations.FixedTimeEquals(ticketHash, RegistrationTicketHash);

    public void CompleteRegistration(DateTimeOffset now) => RegistrationCompletedAt = now;
}
