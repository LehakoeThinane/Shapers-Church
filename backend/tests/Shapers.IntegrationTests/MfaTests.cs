using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Shapers.Identity.Api;

namespace Shapers.IntegrationTests;

public sealed class MfaTests(MfaApiFactory api) : IClassFixture<MfaApiFactory>
{
    [Fact]
    public async Task Sensitive_screens_need_a_second_factor()
    {
        var admin = await api.SignInAdminAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/admin/people")).StatusCode);

        // Non-sensitive admin screens still work, so the admin can reach 2FA set-up.
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/admin/campuses")).StatusCode);

        var setup = await (await admin.PostAsync("/api/auth/2fa/setup", null)).ReadAsync<AuthenticatorSetup>();
        var secret = setup.SharedKey.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        await (await admin.PostJsonAsync("/api/auth/2fa/enable", new { code = Totp.Now(secret) })).ReadAsync<RecoveryCodes>();

        // Enabling 2FA upgrades the current session.
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/admin/people")).StatusCode);

        // A fresh sign-in now needs the code.
        var next = api.Browser();
        var passwordOnly = await (await next.PostAsJsonAsync("/api/auth/staff/login", new { email = ApiFactory.AdminEmail, password = ApiFactory.AdminPassword }))
            .ReadAsync<StaffLoginResponse>();
        Assert.Equal(StaffLoginStatus.TwoFactorRequired, passwordOnly.Status);
    }
}

/// <summary>RFC 6238 TOTP (SHA-1, 30 s, 6 digits), as used by authenticator apps.</summary>
internal static class Totp
{
    public static string Now(string base32Secret)
    {
        var key = Base32Decode(base32Secret);
        var counter = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        var message = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(message);
        }

#pragma warning disable CA5350 // TOTP is defined over HMAC-SHA1.
        var hash = HMACSHA1.HashData(key, message);
#pragma warning restore CA5350
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }

    private static byte[] Base32Decode(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var output = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in input.TrimEnd('='))
        {
            buffer = (buffer << 5) | alphabet.IndexOf(c, StringComparison.Ordinal);
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }

        return [.. output];
    }
}
