using Microsoft.EntityFrameworkCore;
using Shapers.Communications.Contracts;
using Shapers.Communications.Domain;
using Shapers.People.Contracts;
using Shapers.Platform.Auditing;
using Shapers.Platform.Authorization;
using Shapers.Platform.Email;

namespace Shapers.Communications.Application;

/// <summary>
/// Every minute: sends queued push notifications and emails that are due. Consent is checked again at sending
/// time, so withdrawing it stops messages already in the queue.
/// </summary>
public sealed class DeliveryJob(
    ICommunicationsDb db,
    IPushSender push,
    IEmailSender email,
    Unsubscribe unsubscribe,
    IPeopleDirectory people,
    TimeProvider clock)
{
    private const int BatchSize = 100;

    /// <summary>Lock screens show about two lines; the full text is in the inbox.</summary>
    private const int PushBodyLength = 180;

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var due = await db.Notifications
            .Where(n => n.Deliveries.Any(d => d.Status == DeliveryStatus.Pending && d.NotBefore <= now))
            .OrderBy(n => n.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
        if (due.Count == 0)
        {
            return 0;
        }

        var dueDeliveries = due.SelectMany(n => n.Deliveries.Where(d => d.Status == DeliveryStatus.Pending && d.NotBefore <= now).Select(d => (Notification: n, Delivery: d))).ToList();
        await SendPushAsync(dueDeliveries.Where(x => x.Delivery.Channel == Channel.Push).ToList(), now, cancellationToken);
        await SendEmailAsync(dueDeliveries.Where(x => x.Delivery.Channel == Channel.Email).ToList(), now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return dueDeliveries.Count;
    }

    private async Task SendPushAsync(List<(Notification Notification, Delivery Delivery)> items, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return;
        }

        var personIds = items.Select(x => x.Notification.PersonId).Distinct().ToList();
        var devices = await db.Devices.Where(d => d.DisabledAt == null && personIds.Contains(d.PersonId)).ToListAsync(cancellationToken);
        var consent = await people.WithConsentAsync(personIds, CommunicationConsents.Push, cancellationToken);

        var outgoing = new List<(Delivery Delivery, Device Device, PushMessage Message)>();
        foreach (var (notification, delivery) in items)
        {
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
                var body = notification.Body.Length <= PushBodyLength ? notification.Body : string.Concat(notification.Body.AsSpan(0, PushBodyLength - 1), "…");
                outgoing.AddRange(phones.Select(p => (delivery, p, new PushMessage(p.Token, notification.Title, body, notification.Link))));
            }
        }

        if (outgoing.Count == 0)
        {
            return;
        }

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

    private async Task SendEmailAsync(List<(Notification Notification, Delivery Delivery)> items, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return;
        }

        var personIds = items.Select(x => x.Notification.PersonId).Distinct().ToList();
        var addresses = await people.GetManyAsync(personIds, cancellationToken);
        var consent = await people.WithConsentAsync(personIds, CommunicationConsents.Email, cancellationToken);

        foreach (var (notification, delivery) in items)
        {
            var address = addresses.GetValueOrDefault(notification.PersonId)?.Email;
            if (!consent.Contains(notification.PersonId))
            {
                delivery.Skip("No consent to email.");
                continue;
            }

            if (address is null)
            {
                delivery.Skip("No email address.");
                continue;
            }

            var optOut = unsubscribe.LinkFor(notification.PersonId, notification.Topic);
            var paragraphs = notification.Body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            var footer = $"You're receiving this because you asked Shapers Church to email you. Unsubscribe: {optOut}";
            try
            {
                await email.SendAsync(
                    new EmailMessage(
                        address,
                        notification.Title,
                        string.Join("\n\n", paragraphs.Append(footer)),
                        EmailLayout.Html(notification.Title, paragraphs.Append(footer), "Unsubscribe from these emails", optOut)),
                    cancellationToken);
                delivery.Sent(null, now);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                delivery.Failed(ex.Message, now);
            }
        }
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
