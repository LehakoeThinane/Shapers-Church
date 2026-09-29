using Microsoft.EntityFrameworkCore;
using Shapers.Events.Domain;

namespace Shapers.Events.Application;

/// <summary>Builds event and registration read models, including live seat counts.</summary>
public sealed class EventReader(IEventsDb db, TimeProvider clock)
{
    public Task<int> ConfirmedSeatsAsync(Guid eventId, CancellationToken cancellationToken) =>
        db.Registrations.Where(r => r.EventId == eventId && r.Status == RegistrationStatus.Confirmed).SelectMany(r => r.Attendees).CountAsync(cancellationToken);

    public Task<bool> AnyoneWaitingAsync(Guid eventId, CancellationToken cancellationToken) =>
        db.Registrations.AnyAsync(r => r.EventId == eventId && r.Status == RegistrationStatus.Waitlisted, cancellationToken);

    public async Task<EventDto> ToDtoAsync(Event e, CancellationToken cancellationToken)
    {
        var seats = await ConfirmedSeatsAsync(e.Id, cancellationToken);
        var waiting = await AnyoneWaitingAsync(e.Id, cancellationToken);
        return ToDto(e, seats, waiting);
    }

    public EventDto ToDto(Event e, int confirmedSeats, bool anyoneWaiting)
    {
        var closed = e.RegistrationClosedReason(clock.GetUtcNow());
        int? seatsLeft = e.Capacity is null ? null : Seating.SeatsLeft(e, confirmedSeats);
        var full = seatsLeft == 0 || anyoneWaiting;
        if (closed is null && full && !e.WaitlistEnabled && e.Capacity is not null)
        {
            closed = "Sorry, this event is full.";
        }

        return new EventDto(
            e.Id,
            e.Title,
            e.Slug,
            e.Summary,
            e.Description,
            e.StartsAt,
            e.EndsAt,
            e.Location is { } l ? new LocationDto(l.Name, l.Address) : null,
            e.ImageUrl,
            e.Visibility,
            e.RegistrationRequired,
            closed is null,
            closed,
            seatsLeft,
            closed is null && full && e.WaitlistEnabled,
            e.MaxPerRegistration,
            e.Questions.OrderBy(q => q.Order).Select(q => new QuestionDto(q.Id, q.Label, q.Required)).ToList());
    }

    public async Task<RegistrationDto> ToDtoAsync(Registration r, Event e, CancellationToken cancellationToken)
    {
        int? position = null;
        if (r.Status == RegistrationStatus.Waitlisted)
        {
            position = await db.Registrations.CountAsync(
                x => x.EventId == r.EventId && x.Status == RegistrationStatus.Waitlisted && x.CreatedAt < r.CreatedAt, cancellationToken) + 1;
        }

        return new RegistrationDto(
            r.Id,
            e.Id,
            e.Title,
            e.Slug,
            e.StartsAt,
            e.Location is { } l ? new LocationDto(l.Name, l.Address) : null,
            r.Status,
            position,
            r.Status == RegistrationStatus.Confirmed
                ? r.Attendees.Select(a => new TicketDto(a.Id, a.Name, a.TicketCode, a.CheckedInAt)).ToList()
                : r.Attendees.Select(a => new TicketDto(a.Id, a.Name, string.Empty, null)).ToList(),
            r.CreatedAt);
    }
}
