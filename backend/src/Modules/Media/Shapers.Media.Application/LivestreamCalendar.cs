using Microsoft.EntityFrameworkCore;
using Shapers.Media.Domain;
using Shapers.Platform.Calendar;

namespace Shapers.Media.Application;

/// <summary>Scheduled and live streams are public: anyone may see when the church is streaming.</summary>
public sealed class LivestreamCalendar(IMediaDb db) : ICalendarSource
{
    public async Task<IReadOnlyList<CalendarEntry>> ListAsync(CalendarQuery query, CancellationToken cancellationToken)
    {
        var streams = await db.Livestreams.AsNoTracking()
            .Where(l => (l.Status == LivestreamStatus.Scheduled || l.Status == LivestreamStatus.Live)
                && l.ScheduledStart >= query.From && l.ScheduledStart < query.To)
            .ToListAsync(cancellationToken);

        return [.. streams.Select(l => new CalendarEntry(CalendarEntryKind.Livestream, l.Id, l.Title, l.ScheduledStart, null, "Online", null, Draft: false, Mine: false, Public: true))];
    }
}
