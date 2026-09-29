using System.Security.Cryptography;
using System.Text;
using Shapers.Identity.Domain;

namespace Shapers.Identity.Tests;

public sealed class RefreshTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(60);

    [Fact]
    public void Rotation_retires_the_old_token_and_keeps_the_family()
    {
        var first = RefreshToken.Issue(Guid.NewGuid(), "hash-1", "otp", "iPhone", Now, Lifetime);

        var second = first.Rotate("hash-2", Now.AddMinutes(20), Lifetime);

        Assert.False(first.IsActiveAt(Now.AddMinutes(20)));
        Assert.Equal("rotated", first.RevocationReason);
        Assert.Equal(second.Id, first.ReplacedById);
        Assert.Equal(first.FamilyId, second.FamilyId);
        Assert.Equal(first.AuthenticationMethods, second.AuthenticationMethods);
        Assert.True(second.IsActiveAt(Now.AddMinutes(20)));
    }

    [Fact]
    public void A_retired_token_cannot_be_rotated_again()
    {
        var first = RefreshToken.Issue(Guid.NewGuid(), "hash-1", "otp", null, Now, Lifetime);
        first.Rotate("hash-2", Now, Lifetime);

        Assert.Throws<InvalidOperationException>(() => first.Rotate("hash-3", Now, Lifetime));
    }

    [Fact]
    public void Expired_token_is_inactive()
    {
        var token = RefreshToken.Issue(Guid.NewGuid(), "hash", "otp", null, Now, Lifetime);

        Assert.True(token.IsActiveAt(Now + Lifetime - TimeSpan.FromSeconds(1)));
        Assert.False(token.IsActiveAt(Now + Lifetime));
    }
}

public sealed class OtpChallengeTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 8, 0, 0, TimeSpan.Zero);

    private static byte[] Hash(string code) => SHA256.HashData(Encoding.UTF8.GetBytes(code));

    private static OtpChallenge Challenge(string code = "123456") =>
        OtpChallenge.Create(Guid.NewGuid(), "+27821234567", Hash(code), Now, "127.0.0.1");

    [Fact]
    public void Correct_code_verifies_once()
    {
        var challenge = Challenge();

        Assert.Equal(OtpVerification.Success, challenge.Verify(Hash("123456"), Now.AddMinutes(1)));
        Assert.Equal(OtpVerification.AlreadyUsed, challenge.Verify(Hash("123456"), Now.AddMinutes(1)));
    }

    [Fact]
    public void Code_expires()
    {
        Assert.Equal(OtpVerification.Expired, Challenge().Verify(Hash("123456"), Now + OtpChallenge.Lifetime));
    }

    [Fact]
    public void Too_many_wrong_codes_locks_the_challenge_even_for_the_right_code()
    {
        var challenge = Challenge();
        for (var i = 1; i < OtpChallenge.MaxAttempts; i++)
        {
            Assert.Equal(OtpVerification.Invalid, challenge.Verify(Hash("000000"), Now));
        }

        Assert.Equal(OtpVerification.TooManyAttempts, challenge.Verify(Hash("000000"), Now));
        Assert.Equal(OtpVerification.TooManyAttempts, challenge.Verify(Hash("123456"), Now));
    }

    [Fact]
    public void Registration_ticket_needs_a_verified_code_matching_ticket_and_open_window()
    {
        var challenge = Challenge();
        Assert.Throws<InvalidOperationException>(() => challenge.AttachRegistrationTicket(Hash("ticket")));

        challenge.Verify(Hash("123456"), Now);
        challenge.AttachRegistrationTicket(Hash("ticket"));

        Assert.True(challenge.CanRegister(Hash("ticket"), Now.AddMinutes(5)));
        Assert.False(challenge.CanRegister(Hash("other"), Now.AddMinutes(5)));
        Assert.False(challenge.CanRegister(Hash("ticket"), Now + OtpChallenge.RegistrationWindow));

        challenge.CompleteRegistration(Now.AddMinutes(6));
        Assert.False(challenge.CanRegister(Hash("ticket"), Now.AddMinutes(7)));
    }
}

public sealed class RoleTests
{
    [Fact]
    public void System_roles_cannot_be_edited_by_staff()
    {
        var role = Role.Create("Church administrator", null, ["a.b.c"], isSystem: true);

        Assert.Throws<DomainRuleException>(() => role.Update("Renamed", null, []));
    }

    [Fact]
    public void Permissions_are_replaced_not_appended()
    {
        var role = Role.Create("Custom", null, ["a.b.c", "d.e.f"]);

        role.Update("Custom", null, ["d.e.f", "g.h.i"]);

        Assert.Equal(["d.e.f", "g.h.i"], role.PermissionKeys.Order(StringComparer.Ordinal));
    }
}
