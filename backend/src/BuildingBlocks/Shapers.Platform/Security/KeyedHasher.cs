using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Shapers.Platform.Security;

/// <summary>
/// HMAC-SHA256 with a server-side key, for short secrets such as email codes and link keys: a copy of the
/// database alone can't be used to guess or reverse them.
/// </summary>
public interface IKeyedHasher
{
    string Hash(string purpose, string value);

    /// <summary>A random URL-safe secret, e.g. for a "manage my booking" link.</summary>
    string NewSecret(int bytes = 24);
}

internal sealed partial class KeyedHasher : IKeyedHasher
{
    private readonly byte[] _key;

    public KeyedHasher(IConfiguration configuration, IHostEnvironment environment, ILogger<KeyedHasher> logger)
    {
        var configured = configuration["Security:HashKey"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            _key = Convert.FromBase64String(configured);
            if (_key.Length < 32)
            {
                throw new InvalidOperationException("Security:HashKey must be at least 32 bytes.");
            }
        }
        else if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
        {
            LogEphemeral(logger);
            _key = RandomNumberGenerator.GetBytes(32);
        }
        else
        {
            throw new InvalidOperationException("Security:HashKey is not configured.");
        }
    }

    public string Hash(string purpose, string value) =>
        Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"{purpose}|{value}")));

    public string NewSecret(int bytes = 24) =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [LoggerMessage(Level = LogLevel.Warning, Message = "Security:HashKey is not set; using a temporary key. Guest links stop working after a restart.")]
    private static partial void LogEphemeral(ILogger logger);
}
