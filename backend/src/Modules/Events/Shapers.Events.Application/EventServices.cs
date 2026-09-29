using Microsoft.EntityFrameworkCore;
using Shapers.Church.Contracts;
using Shapers.Events.Contracts;
using Shapers.Events.Domain;
using Shapers.Platform.Auditing;
using Shapers.Platform.Authorization;

namespace Shapers.Events.Application;

public sealed class EventAdminService(
    IEventsDb db,
    EventReader reader,
    IAuthorizer authorizer,
    IChurchDirectory church,
    IAuditLog audit,
    TimeProvider clock)
{
    private static readonly Error NotFound = Error.NotFound("events.not_found", "Event not found.");

    public async Task<IReadOnlyList<EventAdminDto>> ListAsync(CancellationToken cancellationToken)
    {
        var scopes = await authorizer.ScopesForAsync(EventsPermissions.Edit, cancellationToken);
        var checkInScopes = await authorizer.ScopesForAsync(EventsPermissions.CheckIn, cancellationToken);
        var since = clock.GetUtcNow().AddDays(-60);
        var events = await db.Events.AsNoTracking()
            .WithinScopes(e => e.Scope, [.. scopes, .. checkInScopes])
            .Where(e => e.EndsAt >= since)
            .OrderBy(e => e.StartsAt)
            .Take(100)
            .ToListAsync(cancellationToken);

        var result = new List<EventAdminDto>();
        foreach (var e in events)
        {
            result.Add(await ToAdminDtoAsync(e, cancellationToken));
        }

        return result;
    }

    public async Task<Result<EventAdminDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var e = await db.Events.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        return e is null || !(await CanAsync(EventsPermissions.Edit, e, cancellationToken) || await CanAsync(EventsPermissions.CheckIn, e, cancellationToken))
            ? NotFound
            : await ToAdminDtoAsync(e, cancellationToken);
    }

    public async Task<Result<EventAdminDto>> CreateAsync(SaveEventRequest request, CancellationToken cancellationToken)
    {
        ScopePath scope;
        if (string.IsNullOrWhiteSpace(request.Scope))
        {
            var campuses = await church.GetCampusesAsync(cancellationToken);
            var primary = campuses.FirstOrDefault(c => c.IsPrimary);
            scope = ScopePath.Parse(primary?.Scope ?? (await church.GetRootScopeAsync(cancellationToken)).Path);
        }
        else if (!ScopePath.TryParse(request.Scope, out scope) || !await church.ScopeExistsAsync(scope.Value, cancellationToken))
        {
            return new Error("events.scope_invalid", "Choose the church, a campus or a ministry.");
        }

        if (!await authorizer.CanAsync(EventsPermissions.Edit, scope, cancellationToken))
        {
            return Error.Forbidden("events.forbidden", "You can't create events here.");
        }

        var now = clock.GetUtcNow();
        var e = Event.Create(request.Title, scope, request.StartsAt, request.EndsAt, now);
        var slug = e.Slug;
        for (var n = 2; await db.Events.AnyAsync(x => x.Slug == slug, cancellationToken); n++)
        {
            slug = $"{e.Slug}-{n}";
        }

        e.UseSlug(slug);
        Apply(e, request, now);
        db.Events.Add(e);
        await db.SaveChangesAsync(cancellationToken);
        await AuditAsync("events.event.created", e, cancellationToken);
        return await ToAdminDtoAsync(e, cancellationToken);
    }

    public Task<Result<EventAdminDto>> UpdateAsync(Guid id, SaveEventRequest request, CancellationToken cancellationToken) =>
        ChangeAsync(id, EventsPermissions.Edit, "events.event.updated", (e, now) => Apply(e, request, now), cancellationToken);

    public Task<Result<EventAdminDto>> PublishAsync(Guid id, CancellationToken cancellationToken) =>
        ChangeAsync(id, EventsPermissions.Publish, "events.event.published", (e, now) => e.Publish(now), cancellationToken);

    public Task<Result<EventAdminDto>> UnpublishAsync(Guid id, CancellationToken cancellationToken) =>
        ChangeAsync(id, EventsPermissions.Publish, "events.event.unpublished", (e, now) => e.Unpublish(now), cancellationToken);

    public Task<Result<EventAdminDto>> CancelAsync(Guid id, CancellationToken cancellationToken) =>
        ChangeAsync(id, EventsPermissions.Publish, "events.event.cancelled", (e, now) => e.Cancel(now), cancellationToken);

    private async Task<Result<EventAdminDto>> ChangeAsync(Guid id, string permission, string auditAction, Action<Event, DateTimeOffset> change, CancellationToken cancellationToken)
    {
        var e = await db.Events.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (e is null || !await CanAsync(EventsPermissions.Edit, e, cancellationToken))
        {
            return NotFound;
        }

        if (!await CanAsync(permission, e, cancellationToken))
        {
            return Error.Forbidden("events.forbidden", "You can edit this event but not publish or cancel it.");
        }

        change(e, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await AuditAsync(auditAction, e, cancellationToken);
        return await ToAdminDtoAsync(e, cancellationToken);
    }

    private static void Apply(Event e, SaveEventRequest r, DateTimeOffset now)
    {
        e.UpdateDetails(r.Title, r.Summary, r.Description, r.StartsAt, r.EndsAt,
            r.Location is null ? null : new EventLocation(r.Location.Name, r.Location.Address), r.ImageUrl, r.Visibility, now);
        e.ConfigureRegistration(r.RegistrationRequired, r.RegistrationOpensAt, r.RegistrationClosesAt, r.Capacity, r.WaitlistEnabled, r.MaxPerRegistration, now);
        e.SetQuestions(r.Questions.Select(q => (q.Id, q.Label, q.Required)).ToList(), now);
    }

    private async Task<EventAdminDto> ToAdminDtoAsync(Event e, CancellationToken cancellationToken)
    {
        var counts = await db.Registrations.AsNoTracking()
            .Where(r => r.EventId == e.Id && r.Status != RegistrationStatus.Cancelled)
            .SelectMany(r => r.Attendees.Select(a => new { r.Status, a.CheckedInAt }))
            .ToListAsync(cancellationToken);
        var confirmed = counts.Count(c => c.Status == RegistrationStatus.Confirmed);
        var waitlisted = counts.Count(c => c.Status == RegistrationStatus.Waitlisted);
        return new EventAdminDto(
            reader.ToDto(e, confirmed, waitlisted > 0),
            e.Status,
            e.Scope,
            e.RegistrationOpensAt,
            e.RegistrationClosesAt,
            e.Capacity,
            e.WaitlistEnabled,
            confirmed,
            waitlisted,
            counts.Count(c => c.CheckedInAt is not null));
    }

    private Task<bool> CanAsync(string permission, Event e, CancellationToken cancellationToken) =>
        authorizer.CanAsync(permission, ScopePath.Parse(e.Scope), cancellationToken);

    private Task AuditAsync(string action, Event e, CancellationToken cancellationToken) =>
        audit.RecordAsync(new AuditRecord(action, "event", e.Id.ToString(), ScopePath.Parse(e.Scope), new { e.Title }), cancellationToken);
}

/// <summary>Published events anyone can see. Members-only events appear once signed in.</summary>
public sealed class PublicEventService(IEventsDb db, EventReader reader, ICurrentUser currentUser, TimeProvider clock)
{
    public async Task<IReadOnlyList<EventDto>> UpcomingAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var signedIn = currentUser.IsAuthenticated;
        var events = await db.Events.AsNoTracking()
            .Where(e => e.Status == EventStatus.Published && e.EndsAt >= now && (signedIn || e.Visibility == EventVisibility.Public))
            .OrderBy(e => e.StartsAt)
            .Take(50)
            .ToListAsync(cancellationToken);

        var result = new List<EventDto>();
        foreach (var e in events)
        {
            result.Add(await reader.ToDtoAsync(e, cancellationToken));
        }

        return result;
    }

    public async Task<Result<EventDto>> GetAsync(string slug, CancellationToken cancellationToken)
    {
        var e = await db.Events.AsNoTracking().SingleOrDefaultAsync(x => x.Slug == slug && x.Status != EventStatus.Draft, cancellationToken);
        return e is null || (e.Visibility == EventVisibility.Members && !currentUser.IsAuthenticated)
            ? Error.NotFound("events.not_found", "Event not found.")
            : await reader.ToDtoAsync(e, cancellationToken);
    }
}
