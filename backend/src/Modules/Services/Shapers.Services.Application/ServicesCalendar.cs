using Microsoft.EntityFrameworkCore;
using Shapers.Platform.Authorization;
using Shapers.Platform.Calendar;
using Shapers.Services.Contracts;
using Shapers.Services.Domain;

namespace Shapers.Services.Application;

/// <summary>
/// Service plans for staff who plan or schedule services in that scope, and each person's own serving dates.
/// Plans and who serves are internal, so nothing here is public.
/// </summary>
public sealed class ServicesCalendar(IServicesDb db, IAuthorizer authorizer, ICurrentUser currentUser) : ICalendarSource
{
    public async Task<IReadOnlyList<CalendarEntry>> ListAsync(CalendarQuery query, CancellationToken cancellationToken)
    {
        var firstDay = ChurchTime.DateOf(query.From);
        var lastDay = ChurchTime.DateOf(query.To);
        var entries = new List<CalendarEntry>();

        var scopes = (await authorizer.ScopesForAsync(ServicesPermissions.PlansEdit, cancellationToken))
            .Concat(await authorizer.ScopesForAsync(ServicesPermissions.Schedule, cancellationToken))
            .ToList();
        if (scopes.Count > 0)
        {
            var plans = await db.Plans.AsNoTracking()
                .Where(p => p.Date >= firstDay && p.Date <= lastDay)
                .WithinScopes(p => p.Scope, scopes)
                .ToListAsync(cancellationToken);
            entries.AddRange(plans.Select(p => new CalendarEntry(
                CalendarEntryKind.Service, p.Id, p.Title, ChurchTime.At(p.Date, p.StartTime), null, null, null, Draft: false, Mine: false, Public: false)));
        }

        if (currentUser.PersonId is { } personId)
        {
            var serving = await (
                from a in db.Assignments.AsNoTracking()
                join p in db.Plans.AsNoTracking() on a.PlanId equals p.Id
                join position in db.Positions.AsNoTracking() on a.PositionId equals position.Id
                where a.PersonId == personId && a.Status != AssignmentStatus.Declined && a.Date >= firstDay && a.Date <= lastDay
                select new { a.Id, PlanTitle = p.Title, p.Date, p.StartTime, Position = position.Name, a.Status })
                .ToListAsync(cancellationToken);
            entries.AddRange(serving.Select(s => new CalendarEntry(
                CalendarEntryKind.Serving,
                s.Id,
                s.Status == AssignmentStatus.Pending ? $"Asked to serve: {s.Position}, {s.PlanTitle}" : $"Serving: {s.Position}, {s.PlanTitle}",
                ChurchTime.At(s.Date, s.StartTime),
                null,
                null,
                null,
                Draft: false,
                Mine: true,
                Public: false)));
        }

        return [.. entries.Where(e => e.StartsAt >= query.From && e.StartsAt < query.To)];
    }
}
