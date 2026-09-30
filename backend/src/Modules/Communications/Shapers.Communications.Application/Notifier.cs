using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shapers.Communications.Domain;
using Shapers.People.Contracts;

namespace Shapers.Communications.Application;

/// <summary>
/// Decides who gets a notification and on which channels, then queues it. Every recipient gets an inbox item;
/// push is added only when they have an active device, consent to push, and the topic switched on; email (for
/// messages that allow it) only with an address, consent to email and the topic switched on for email.
/// Sending happens in <see cref="DeliveryJob"/>.
/// </summary>
public sealed class Notifier(ICommunicationsDb db, IPeopleDirectory people, IOptions<CommunicationsOptions> options, TimeProvider clock)
{
    public sealed record Message(Topic Topic, string Title, string Body, string? Link, string SourceKey, bool Urgent = false, bool Email = false);

    /// <summary>To specific people, e.g. the person moved off a waiting list. Returns how many were newly notified.</summary>
    public async Task<int> ToPeopleAsync(IReadOnlyCollection<Guid> personIds, Message message, CancellationToken cancellationToken)
    {
        var recipients = personIds.Distinct().ToList();
        if (recipients.Count == 0)
        {
            return 0;
        }

        var already = await db.Notifications.AsNoTracking()
            .Where(n => n.SourceKey == message.SourceKey && recipients.Contains(n.PersonId))
            .Select(n => n.PersonId)
            .ToListAsync(cancellationToken);
        recipients = recipients.Except(already).ToList();
        if (recipients.Count == 0)
        {
            return 0;
        }

        var choices = await db.Preferences.AsNoTracking()
            .Where(p => recipients.Contains(p.PersonId) && p.Topic == message.Topic)
            .ToListAsync(cancellationToken);
        bool Wants(Guid personId, Channel channel) =>
            choices.SingleOrDefault(p => p.PersonId == personId && p.Channel == channel)?.Enabled ?? TopicPreference.Default(message.Topic, channel);

        var withDevice = (await db.Devices.AsNoTracking()
            .Where(d => d.DisabledAt == null && recipients.Contains(d.PersonId))
            .Select(d => d.PersonId)
            .Distinct()
            .ToListAsync(cancellationToken)).ToHashSet();
        var pushable = await people.WithConsentAsync(withDevice, CommunicationConsents.Push, cancellationToken);

        IReadOnlySet<Guid> emailable = new HashSet<Guid>();
        if (message.Email)
        {
            var withEmail = (await people.GetManyAsync(recipients, cancellationToken)).Values.Where(p => p.Email is not null).Select(p => p.Id).ToList();
            emailable = await people.WithConsentAsync(withEmail, CommunicationConsents.Email, cancellationToken);
        }

        var now = clock.GetUtcNow();
        foreach (var personId in recipients)
        {
            var channels = new List<Channel>();
            if (pushable.Contains(personId) && Wants(personId, Channel.Push))
            {
                channels.Add(Channel.Push);
            }

            if (emailable.Contains(personId) && Wants(personId, Channel.Email))
            {
                channels.Add(Channel.Email);
            }

            var sendNow = message.Urgent || !options.Value.QuietHours;
            db.Notifications.Add(Notification.Create(personId, message.Topic, message.Title, message.Body, message.Link, message.SourceKey, sendNow, channels, now));
        }

        await db.SaveChangesAsync(cancellationToken);
        return recipients.Count;
    }

    /// <summary>To every member with the app, optionally only within a scope (e.g. one campus).</summary>
    public async Task<int> ToAppUsersAsync(string? scope, Message message, CancellationToken cancellationToken)
    {
        var personIds = await db.Devices.AsNoTracking()
            .Where(d => d.DisabledAt == null)
            .Select(d => d.PersonId)
            .Distinct()
            .ToListAsync(cancellationToken);
        if (scope is not null && personIds.Count > 0)
        {
            var summaries = await people.GetManyAsync(personIds, cancellationToken);
            personIds = personIds.Where(id => summaries.TryGetValue(id, out var s) && Overlaps(s.Scope, scope)).ToList();
        }

        return await ToPeopleAsync(personIds, message, cancellationToken);
    }

    /// <summary>To everyone in a scope, app or not: announcements reach people by email too.</summary>
    public async Task<int> ToScopeAsync(string scope, Message message, CancellationToken cancellationToken)
    {
        var everyone = await people.InScopeAsync(scope, cancellationToken);
        return await ToPeopleAsync(everyone.Select(p => p.Id).ToList(), message, cancellationToken);
    }

    /// <summary>
    /// A church-wide message reaches someone at a campus; a campus message also reaches people recorded at church
    /// level (not yet placed at a campus).
    /// </summary>
    private static bool Overlaps(string personScope, string scope) =>
        personScope == scope
        || personScope.StartsWith(scope + ".", StringComparison.Ordinal)
        || scope.StartsWith(personScope + ".", StringComparison.Ordinal);
}
