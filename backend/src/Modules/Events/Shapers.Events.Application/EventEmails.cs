using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shapers.Events.Contracts;
using Shapers.Events.Domain;
using Shapers.People.Contracts;
using Shapers.Platform.Email;
using Shapers.Platform.Messaging;

namespace Shapers.Events.Application;

/// <summary>Registration emails. Failures are logged, never shown to the person registering: their booking already stands.</summary>
public sealed partial class EventEmails(IEmailSender sender, IOptions<EventsOptions> options, ILogger<EventEmails> logger)
{
    private static readonly TimeZoneInfo Johannesburg = TimeZoneInfo.FindSystemTimeZoneById("Africa/Johannesburg");
    private static readonly CultureInfo SouthAfrica = CultureInfo.GetCultureInfo("en-ZA");

    public static string When(DateTimeOffset at) =>
        TimeZoneInfo.ConvertTime(at, Johannesburg).ToString("dddd d MMMM yyyy 'at' HH:mm", SouthAfrica);

    public Task SendGuestCodeAsync(string email, string eventTitle, string code, CancellationToken cancellationToken) =>
        SendAsync(new EmailMessage(
            email,
            $"{code} is your Shapers Church code",
            $"Your code to register for {eventTitle} is {code}. It expires in 15 minutes. If you didn't ask for this, ignore this email.",
            EmailLayout.Html("Your registration code", [$"Use this code to register for {eventTitle}:", code, "It expires in 15 minutes. If you didn't ask for this, you can ignore this email."])),
            cancellationToken);

    public Task SendConfirmationAsync(string email, RegistrationDto registration, Event e, string? guestKey, CancellationToken cancellationToken)
    {
        var link = guestKey is null ? null : $"{options.Value.PublicSiteUrl.TrimEnd('/')}/events/tickets/{registration.Id}?key={Uri.EscapeDataString(guestKey)}";
        var where = e.Location is { } l ? $"{l.Name}{(l.Address is null ? string.Empty : $", {l.Address}")}" : "Shapers Church";
        var lines = registration.Status == RegistrationStatus.Waitlisted
            ? new[] { $"You're on the waiting list for {e.Title} ({When(e.StartsAt)}), number {registration.WaitlistPosition}.", "We'll email you as soon as a seat opens up." }
            : new[] { $"You're booked for {e.Title}.", $"When: {When(e.StartsAt)}", $"Where: {where}", $"Tickets: {string.Join(", ", registration.Tickets.Select(t => $"{t.Name} ({t.Code})"))}", "Show your ticket at the door. We look forward to seeing you!" };
        var subject = registration.Status == RegistrationStatus.Waitlisted ? $"Waiting list: {e.Title}" : $"You're booked: {e.Title}";
        return SendAsync(new EmailMessage(email, subject, string.Join("\n", lines.Append(link ?? string.Empty)), EmailLayout.Html(subject, lines, link is null ? null : "View or cancel your booking", link)), cancellationToken);
    }

    public Task SendPromotedAsync(string email, Event e, CancellationToken cancellationToken)
    {
        var lines = new[] { $"Good news: a seat opened up and you're now booked for {e.Title}.", $"When: {When(e.StartsAt)}", "Your tickets are in your earlier email, or in the app under My tickets." };
        return SendAsync(new EmailMessage(email, $"You're in: {e.Title}", string.Join("\n", lines), EmailLayout.Html($"You're in: {e.Title}", lines)), cancellationToken);
    }

    public Task SendCancelledAsync(string email, Event e, CancellationToken cancellationToken)
    {
        var lines = new[] { $"We're sorry: {e.Title} ({When(e.StartsAt)}) has been cancelled.", "Please contact info@shaperschurch.com with any questions." };
        return SendAsync(new EmailMessage(email, $"Cancelled: {e.Title}", string.Join("\n", lines), EmailLayout.Html($"Cancelled: {e.Title}", lines)), cancellationToken);
    }

    public Task SendReminderAsync(string email, Event e, CancellationToken cancellationToken)
    {
        var lines = new[] { $"A reminder that {e.Title} is tomorrow: {When(e.StartsAt)}.", e.Location is { } l ? $"Where: {l.Name}{(l.Address is null ? string.Empty : $", {l.Address}")}" : "See you there!", "Bring your ticket (on your phone is fine)." };
        return SendAsync(new EmailMessage(email, $"Tomorrow: {e.Title}", string.Join("\n", lines), EmailLayout.Html($"Tomorrow: {e.Title}", lines)), cancellationToken);
    }

    private async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        try
        {
            await sender.SendAsync(message, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(logger, message.Subject, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Sending email \"{Subject}\" failed")]
    private static partial void LogFailed(ILogger logger, string subject, Exception exception);
}

/// <summary>Members get their confirmation via the outbox; guests were emailed during registration (their link key isn't stored).</summary>
public sealed class ConfirmMemberRegistration(IEventsDb db, EventReader reader, EventEmails emails, IPeopleDirectory people)
    : IIntegrationEventHandler<EventRegisteredIntegrationEvent>
{
    public async Task HandleAsync(EventRegisteredIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var r = await db.Registrations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == integrationEvent.RegistrationId, cancellationToken);
        if (r is null || r.GuestKeyHash is not null || r.Status == RegistrationStatus.Cancelled)
        {
            return;
        }

        var email = (await people.GetAsync(r.RegistrantPersonId, cancellationToken))?.Email;
        if (email is null)
        {
            return;
        }

        var e = await db.Events.AsNoTracking().SingleAsync(x => x.Id == r.EventId, cancellationToken);
        await emails.SendConfirmationAsync(email, await reader.ToDtoAsync(r, e, cancellationToken), e, null, cancellationToken);
    }
}

public sealed class NotifyWaitlistPromotion(IEventsDb db, EventEmails emails, IPeopleDirectory people) : IIntegrationEventHandler<WaitlistPromotedIntegrationEvent>
{
    public async Task HandleAsync(WaitlistPromotedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var email = (await people.GetAsync(integrationEvent.PersonId, cancellationToken))?.Email;
        if (email is null)
        {
            return;
        }

        var e = await db.Events.AsNoTracking().SingleAsync(x => x.Id == integrationEvent.ChurchEventId, cancellationToken);
        await emails.SendPromotedAsync(email, e, cancellationToken);
    }
}

public sealed class NotifyEventCancelled(IEventsDb db, EventEmails emails, IPeopleDirectory people) : IIntegrationEventHandler<EventCancelledIntegrationEvent>
{
    public async Task HandleAsync(EventCancelledIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var e = await db.Events.AsNoTracking().SingleAsync(x => x.Id == integrationEvent.ChurchEventId, cancellationToken);
        var registrants = await db.Registrations.AsNoTracking()
            .Where(r => r.EventId == e.Id && r.Status != RegistrationStatus.Cancelled)
            .Select(r => r.RegistrantPersonId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var summaries = await people.GetManyAsync(registrants, cancellationToken);
        foreach (var email in summaries.Values.Select(s => s.Email).OfType<string>().Distinct())
        {
            await emails.SendCancelledAsync(email, e, cancellationToken);
        }
    }
}

/// <summary>Hourly: remind confirmed registrants the day before. Each registration is reminded once.</summary>
public sealed class ReminderJob(IEventsDb db, EventEmails emails, IPeopleDirectory people, TimeProvider clock)
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var events = await db.Events.AsNoTracking()
            .Where(e => e.Status == EventStatus.Published && e.StartsAt > now.AddHours(20) && e.StartsAt <= now.AddHours(26))
            .ToListAsync(cancellationToken);
        var sent = 0;
        foreach (var e in events)
        {
            var due = await db.Registrations.Where(r => r.EventId == e.Id && r.Status == RegistrationStatus.Confirmed && r.ReminderSentAt == null).ToListAsync(cancellationToken);
            var summaries = await people.GetManyAsync(due.Select(r => r.RegistrantPersonId).Distinct().ToList(), cancellationToken);
            foreach (var r in due)
            {
                if (summaries.GetValueOrDefault(r.RegistrantPersonId)?.Email is { } email)
                {
                    await emails.SendReminderAsync(email, e, cancellationToken);
                    sent++;
                }

                r.MarkReminderSent(now);
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        return sent;
    }
}
