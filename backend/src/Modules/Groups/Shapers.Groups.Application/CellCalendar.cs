using Microsoft.EntityFrameworkCore;
using Shapers.Groups.Contracts;
using Shapers.Groups.Domain;
using Shapers.Platform.Authorization;
using Shapers.Platform.Calendar;

namespace Shapers.Groups.Application;

/// <summary>
/// Weekly cell meetings. A member sees their own cell's meetings. Staff who manage cells see every cell in their
/// scope when they ask for it (cell membership is sensitive, so this needs the second-factor session like the rest).
/// Only the area is shown, never a home address.
/// </summary>
public sealed class CellCalendar(IGroupsDb db, IAuthorizer authorizer, ICurrentUser currentUser) : ICalendarSource
{
    public async Task<IReadOnlyList<CalendarEntry>> ListAsync(CalendarQuery query, CancellationToken cancellationToken)
    {
        var open = db.Cells.AsNoTracking().Where(c => c.ClosedAt == null && c.MeetingDay != null && c.MeetingTime != null);

        var mine = new List<Cell>();
        if (currentUser.PersonId is { } personId)
        {
            var myCellIds = db.Members.Where(m => m.PersonId == personId && m.LeftAt == null).Select(m => m.CellId);
            mine = await open.Where(c => myCellIds.Contains(c.Id)).ToListAsync(cancellationToken);
        }

        var others = new List<Cell>();
        if (query.AllCells)
        {
            var scopes = await authorizer.ScopesForAsync(GroupsPermissions.CellsManage, cancellationToken);
            if (scopes.Count > 0)
            {
                var mineIds = mine.Select(c => c.Id).ToList();
                others = await open.WithinScopes(c => c.Scope, scopes).Where(c => !mineIds.Contains(c.Id)).ToListAsync(cancellationToken);
            }
        }

        return [.. mine.SelectMany(c => Meetings(c, query, mine: true)).Concat(others.SelectMany(c => Meetings(c, query, mine: false)))];
    }

    private static IEnumerable<CalendarEntry> Meetings(Cell cell, CalendarQuery query, bool mine)
    {
        var day = ChurchTime.DateOf(query.From);
        var last = ChurchTime.DateOf(query.To);
        day = day.AddDays(((int)cell.MeetingDay!.Value - (int)day.DayOfWeek + 7) % 7);
        for (; day <= last; day = day.AddDays(7))
        {
            var at = ChurchTime.At(day, cell.MeetingTime!.Value);
            if (at >= query.From && at < query.To)
            {
                yield return new CalendarEntry(CalendarEntryKind.Cell, cell.Id, cell.Name, at, null, cell.Area, null, Draft: false, Mine: mine, Public: false);
            }
        }
    }
}
