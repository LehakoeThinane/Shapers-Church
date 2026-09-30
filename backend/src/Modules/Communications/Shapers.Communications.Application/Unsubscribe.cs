using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shapers.Communications.Domain;
using Shapers.Platform.Security;

namespace Shapers.Communications.Application;

public sealed class CommunicationsOptions
{
    public const string SectionName = "Communications";

    /// <summary>Where the API is reachable from the internet, for links in emails (e.g. unsubscribe).</summary>
    public string PublicApiUrl { get; set; } = "http://localhost:5080";

    /// <summary>Hold non-urgent messages from 21:00 to 07:00 (Johannesburg). Only tests switch this off.</summary>
    public bool QuietHours { get; set; } = true;
}

/// <summary>
/// One-click unsubscribe from emails about a topic, as South African law requires for direct marketing (POPIA s69,
/// ECTA s45). The link is signed with the server's key, so it can't be forged to change someone else's settings.
/// </summary>
public sealed class Unsubscribe(ICommunicationsDb db, IKeyedHasher hasher, IOptions<CommunicationsOptions> options, TimeProvider clock)
{
    private const string Purpose = "communications.unsubscribe";

    public string LinkFor(Guid personId, Topic topic) =>
        $"{options.Value.PublicApiUrl.TrimEnd('/')}/api/unsubscribe?p={personId:N}&t={topic}&s={Sign(personId, topic)}";

    public async Task<bool> ApplyAsync(Guid personId, Topic topic, string signature, CancellationToken cancellationToken)
    {
        var expected = Encoding.ASCII.GetBytes(Sign(personId, topic));
        if (!CryptographicOperations.FixedTimeEquals(expected, Encoding.ASCII.GetBytes(signature ?? string.Empty)))
        {
            return false;
        }

        var now = clock.GetUtcNow();
        var existing = await db.Preferences.SingleOrDefaultAsync(p => p.PersonId == personId && p.Topic == topic && p.Channel == Channel.Email, cancellationToken);
        if (existing is null)
        {
            db.Preferences.Add(TopicPreference.Set(personId, topic, Channel.Email, false, now));
        }
        else
        {
            existing.Change(false, now);
        }

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private string Sign(Guid personId, Topic topic) => hasher.Hash(Purpose, $"{personId:N}:{topic}")[..32];
}
