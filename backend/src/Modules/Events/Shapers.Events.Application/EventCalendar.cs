using Microsoft.EntityFrameworkCore;
using Shapers.Events.Contracts;
using Shapers.Events.Domain;
using Shapers.Platform.Authorization;
using Shapers.Platform.Calendar;

namespace Shapers.Events.Application;

/// <summary>
/// Published events, as on the events list: public ones for everyone, members-only ones for anyone signed in.
/// Draft events too for staff who edit events in that scope.
/// </summary>
public sealed class EventCalendar(IEventsDb db, IAuthorizer authorizer, ICurrentUser currentUser) : ICalendarSource
{
    public async Task<IReadOnlyList<CalendarEntry>> ListAsync(CalendarQuery query, CancellationToken cancellationToken)
    {
        var inRange = db.Events.AsNoTracking().Where(e => e.StartsAt < query.To && e.EndsAt >= query.From);
        var signedIn = currentUser.IsAuthenticated;

        var published = await inRange
            .Where(e => e.Status == EventStatus.Published && (signedIn || e.Visibility == EventVisibility.Public))
            .ToListAsync(cancellationToken);
        var editScopes = await authorizer.ScopesForAsync(EventsPermissions.Edit, cancellationToken);
        var drafts = editScopes.Count == 0
            ? []
            : await inRange.Where(e => e.Status == EventStatus.Draft).WithinScopes(e => e.Scope, editScopes).ToListAsync(cancellationToken);

        return
        [
            .. published.Concat(drafts).Select(e => new CalendarEntry(
                CalendarEntryKind.Event,
                e.Id,
                e.Title,
                e.StartsAt,
                e.EndsAt,
                e.Location?.Name,
                e.Slug,
                Draft: e.Status == EventStatus.Draft,
                Mine: false,
                Public: e.Status == EventStatus.Published && e.Visibility == EventVisibility.Public)),
        ];
    }
}
