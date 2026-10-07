using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Shapers.Platform.Calendar;

namespace Shapers.Api.Hosting;

/// <summary>
/// The church calendar: what every module has on between two dates, for whoever is asking. Visitors see public
/// entries (published events, livestreams); members also see their own cell and serving dates; staff see more
/// according to their permissions. Each <see cref="ICalendarSource"/> decides what the viewer may see.
/// </summary>
internal static class Calendar
{
    /// <summary>About three months: enough for a month view with the days either side, small enough to stay quick.</summary>
    public static readonly TimeSpan MaxRange = TimeSpan.FromDays(93);

    public static IEndpointRouteBuilder MapCalendar(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/calendar", ListAsync)
            .WithTags("Calendar")
            .WithName("GetCalendar")
            .AllowAnonymous();

        // A feed people can subscribe to from their phone's calendar. Public entries only, whoever asks.
        endpoints.MapGet("/calendar.ics", FeedAsync)
            .WithTags("Calendar")
            .WithName("GetCalendarFeed")
            .AllowAnonymous()
            .ExcludeFromDescription();
        return endpoints;
    }

    private static async Task<Results<Ok<IReadOnlyList<CalendarEntry>>, ValidationProblem>> ListAsync(
        DateTimeOffset from, DateTimeOffset to, bool? allCells, IEnumerable<ICalendarSource> sources, CancellationToken cancellationToken)
    {
        if (to <= from || to - from > MaxRange)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["to"] = ["Choose an end after the start, at most three months later."],
            });
        }

        return TypedResults.Ok(await CollectAsync(new CalendarQuery(from, to, allCells ?? false), sources, cancellationToken));
    }

    private static async Task<IResult> FeedAsync(IEnumerable<ICalendarSource> sources, TimeProvider clock, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var entries = new List<CalendarEntry>();
        for (var from = now.AddDays(-14); from < now.AddDays(180); from += MaxRange)
        {
            entries.AddRange(await CollectAsync(new CalendarQuery(from, from + MaxRange, AllCells: false), sources, cancellationToken));
        }

        var ics = new StringBuilder()
            .Append("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//Shapers Church//Calendar//EN\r\nCALSCALE:GREGORIAN\r\n")
            .Append("X-WR-CALNAME:Shapers Church\r\nX-WR-TIMEZONE:Africa/Johannesburg\r\n");
        foreach (var e in entries.Where(IsPublic).DistinctBy(e => (e.Kind, e.RefId)))
        {
            ics.Append("BEGIN:VEVENT\r\n")
                .Append(Line($"UID:{e.Kind.ToString().ToLowerInvariant()}-{e.RefId}@shaperschurch.com"))
                .Append(Line($"DTSTAMP:{Utc(now)}"))
                .Append(Line($"DTSTART:{Utc(e.StartsAt)}"))
                .Append(Line($"DTEND:{Utc(e.EndsAt ?? e.StartsAt.AddHours(2))}"))
                .Append(Line($"SUMMARY:{Escape(e.Kind == CalendarEntryKind.Livestream ? $"{e.Title} (online)" : e.Title)}"));
            if (e.Place is { } place)
            {
                ics.Append(Line($"LOCATION:{Escape(place)}"));
            }

            ics.Append("END:VEVENT\r\n");
        }

        ics.Append("END:VCALENDAR\r\n");
        return Results.Text(ics.ToString(), "text/calendar; charset=utf-8");
    }

    private static async Task<IReadOnlyList<CalendarEntry>> CollectAsync(CalendarQuery query, IEnumerable<ICalendarSource> sources, CancellationToken cancellationToken)
    {
        var entries = new List<CalendarEntry>();
        foreach (var source in sources)
        {
            entries.AddRange(await source.ListAsync(query, cancellationToken));
        }

        return [.. entries.OrderBy(e => e.StartsAt).ThenBy(e => e.Title, StringComparer.Ordinal)];
    }

    private static bool IsPublic(CalendarEntry e) => e.Public;

    private static string Utc(DateTimeOffset at) => at.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    private static string Escape(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace(";", "\\;", StringComparison.Ordinal)
            .Replace(",", "\\,", StringComparison.Ordinal)
            .Replace("\r", "", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);

    /// <summary>Content lines longer than 75 characters continue on the next line after a space (RFC 5545).</summary>
    private static string Line(string content)
    {
        var line = new StringBuilder();
        for (var i = 0; i < content.Length; i += 74)
        {
            line.Append(i == 0 ? "" : " ").Append(content.AsSpan(i, Math.Min(74, content.Length - i))).Append("\r\n");
        }

        return line.ToString();
    }
}
