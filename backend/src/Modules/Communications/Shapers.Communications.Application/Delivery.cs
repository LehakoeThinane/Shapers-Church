using Microsoft.EntityFrameworkCore;
using Shapers.Communications.Contracts;
using Shapers.Communications.Domain;
using Shapers.People.Contracts;
using Shapers.Platform.Auditing;
using Shapers.Platform.Authorization;

namespace Shapers.Communications.Application;

/// <summary>
/// Every minute: sends queued push notifications that are due. Consent is checked again at sending time,
/// so withdrawing it stops messages already in the queue.
/// </summary>
public sealed class DeliveryJob(ICommunicationsDb db, IPushSender push, IPeopleDirectory people, TimeProvider clock)
{
    private const int BatchSize = 100;

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var due = await db.Notifications
            .Where(n => n.Deliveries.Any(d => d.Channel == Channel.Push && d.Status == DeliveryStatus.Pending && d.NotBefore <= now))
            .OrderBy(n => n.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
        if (due.Count == 0)
        {
            return 0;
        }

        var personIds = due.Select(n => n.PersonId).Distinct().ToList();
        var devices = await db.Devices.Where(d => d.DisabledAt == null && personIds.Contains(d.PersonId)).ToListAsync(cancellationToken);
        var consent = await people.WithConsentAsync(personIds, CommunicationConsents.Push, cancellationToken);

        var outgoing = new List<(Delivery Delivery, Device Device, PushMessage Message)>();
        foreach (var notification in due)
        {
            var delivery = notification.Deliveries.Single(d => d.Channel == Channel.Push);
            var phones = devices.Where(d => d.PersonId == notification.PersonId).ToList();
            if (!consent.Contains(notification.PersonId))
            {
                delivery.Skip("No consent to push notifications.");
            }
            else if (phones.Count == 0)
            {
                delivery.Skip("No phone registered.");
            }
            else
            {
                outgoing.AddRange(phones.Select(p => (delivery, p, new PushMessage(p.Token, notification.Title, notification.Body, notification.Link))));
            }
        }

        if (outgoing.Count > 0)
        {
            var results = await push.SendAsync(outgoing.Select(o => o.Message).ToList(), cancellationToken);
            // A person with two phones is delivered if either phone accepted it.
            foreach (var group in outgoing.Zip(results).GroupBy(x => x.First.Delivery))
            {
                foreach (var ((_, device, _), result) in group.Where(x => x.Second.DeviceGone))
                {
                    device.Disable(result.Error ?? "Device no longer registered", now);
                }

                var accepted = group.FirstOrDefault(x => x.Second.Ok);
                if (accepted.Second is not null)
                {
                    group.Key.Sent(accepted.Second.ProviderReference, now);
                }
                else if (group.All(x => x.Second.DeviceGone))
                {
                    group.Key.Skip("The app is no longer on this phone.");
                }
                else
                {
                    group.Key.Failed(group.First(x => !x.Second.Ok).Second.Error ?? "Push failed", now);
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return due.Count;
    }
}

/// <summary>Staff with communications.deliveries.view: what was sent recently and whether it arrived. Audited.</summary>
public sealed class DeliveryLog(ICommunicationsDb db, IAuthorizer authorizer, IAuditLog audit)
{
    public async Task<Result<IReadOnlyList<DeliveryLogDto>>> RecentAsync(CancellationToken cancellationToken)
    {
        if (!await authorizer.HasAnywhereAsync(CommunicationsPermissions.DeliveriesView, cancellationToken))
        {
            return Error.Forbidden("communications.forbidden", "You can't see the delivery log.");
        }

        var rows = await db.Notifications.AsNoTracking()
            .OrderByDescending(n => n.CreatedAt)
            .Take(200)
            .SelectMany(n => n.Deliveries.Select(d => new DeliveryLogDto(n.Id, n.PersonId, n.Topic, n.Title, d.Channel, d.Status, d.Attempts, n.CreatedAt, d.SentAt, d.Error)))
            .ToListAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord("communications.deliveries.viewed", "notification", null, Details: new { count = rows.Count }, IsSensitiveRead: true), cancellationToken);
        return rows;
    }
}
