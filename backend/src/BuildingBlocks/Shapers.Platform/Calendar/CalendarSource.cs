namespace Shapers.Platform.Calendar;

public enum CalendarEntryKind
{
    Event,
    Livestream,
    Service,
    Cell,
    Serving,
}

/// <summary>
/// One thing on the church calendar. <see cref="RefId"/> (and <see cref="Slug"/> for public pages) let each app open
/// the entry in its own screen. <see cref="Mine"/> marks the viewer's own cell meeting or serving date.
/// <see cref="Public"/> is true only for what anyone may see; only those go on the public calendar feed.
/// </summary>
public sealed record CalendarEntry(
    CalendarEntryKind Kind,
    Guid RefId,
    string Title,
    DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt,
    string? Place,
    string? Slug,
    bool Draft,
    bool Mine,
    bool Public);

/// <summary>The dates asked for, and whether a staff member asked to see every cell's meeting.</summary>
public sealed record CalendarQuery(DateTimeOffset From, DateTimeOffset To, bool AllCells);

/// <summary>
/// Each module that has something with a date implements this. The API asks every source and puts the answers
/// together. A source returns only what the current viewer may see: public entries for anyone, the rest checked
/// against the viewer's permissions and scopes. Never include other people's names or contact details.
/// </summary>
public interface ICalendarSource
{
    Task<IReadOnlyList<CalendarEntry>> ListAsync(CalendarQuery query, CancellationToken cancellationToken);
}

public static class ChurchTime
{
    /// <summary>The church's time zone. Service plans and cell meetings are stored as local dates and times.</summary>
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Africa/Johannesburg");

    public static DateTimeOffset At(DateOnly date, TimeOnly time)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, Zone.GetUtcOffset(local));
    }

    public static DateOnly DateOf(DateTimeOffset at) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, Zone).DateTime);
}
