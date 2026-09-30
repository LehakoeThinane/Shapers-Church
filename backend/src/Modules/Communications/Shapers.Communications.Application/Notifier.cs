using Microsoft.EntityFrameworkCore;
using Shapers.Communications.Domain;
using Shapers.People.Contracts;

namespace Shapers.Communications.Application;

/// <summary>
/// Decides who gets a notification and on which channels, then queues it. Every recipient gets an inbox item;
/// push is added only when they have an active device, consent to push, and the topic switched on.
/// Sending happens in <see cref="DeliveryJob"/>.
/// </summary>
public sealed class Notifier(ICommunicationsDb db, IPeopleDirectory people, TimeProvider clock)
{
    public sealed record Message(Topic Topic, string Title, string Body, string? Link, string SourceKey, bool Urgent = false);

    /// <summary>To specific people, e.g. the person moved off a waiting list.</summary>
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

        var withDevice = (await db.Devices.AsNoTracking()
            .Where(d => d.DisabledAt == null && recipients.Contains(d.PersonId))
            .Select(d => d.PersonId)
            .Distinct()
            .ToListAsync(cancellationToken)).ToHashSet();
        var pushConsent = await people.WithConsentAsync(withDevice, CommunicationConsents.Push, cancellationToken);
        var choices = await db.Preferences.AsNoTracking()
            .Where(p => recipients.Contains(p.PersonId) && p.Topic == message.Topic && p.Channel == Channel.Push)
            .ToDictionaryAsync(p => p.PersonId, p => p.Enabled, cancellationToken);
        var pushByDefault = TopicPreference.Default(message.Topic, Channel.Push);

        var now = clock.GetUtcNow();
        foreach (var personId in recipients)
        {
            var push = pushConsent.Contains(personId) && choices.GetValueOrDefault(personId, pushByDefault);
            db.Notifications.Add(Notification.Create(
                personId, message.Topic, message.Title, message.Body, message.Link, message.SourceKey, message.Urgent, push ? [Channel.Push] : [], now));
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
            personIds = personIds
                .Where(id => summaries.TryGetValue(id, out var s) && (s.Scope == scope || s.Scope.StartsWith(scope + ".", StringComparison.Ordinal) || scope.StartsWith(s.Scope + ".", StringComparison.Ordinal)))
                .ToList();
        }

        return await ToPeopleAsync(personIds, message, cancellationToken);
    }
}
