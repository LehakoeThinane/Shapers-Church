namespace Shapers.Services.Domain;

/// <summary>
/// A way of playing a song: its key and tempo, the chord chart the band reads, and a recording to rehearse with.
/// Files live in the platform's media storage; only their links are kept here.
/// </summary>
public sealed record Arrangement(
    Guid Id,
    string Name,
    string? Key,
    int? Bpm,
    string? ChartUrl,
    string? ChartFileName,
    string? AudioUrl,
    string? Notes);

/// <summary>
/// A song the church sings. Lyrics are stored because the church holds a CCLI licence; the CCLI number is what the
/// usage report needs.
/// </summary>
public sealed class Song : AggregateRoot<Guid>
{
    public const int MaxLyrics = 20_000;

    private Song()
    {
    }

    public string Title { get; private set; } = null!;

    public string? Author { get; private set; }

    public string? CcliNumber { get; private set; }

    public List<string> Themes { get; private set; } = [];

    public string? Lyrics { get; private set; }

    /// <summary>A recording to learn it from, e.g. a YouTube link.</summary>
    public string? ReferenceUrl { get; private set; }

    public string Scope { get; private set; } = null!;

    public List<Arrangement> Arrangements { get; private set; } = [];

    public bool IsArchived { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Song Create(string title, ScopePath scope, DateTimeOffset now)
    {
        var song = new Song { Id = Guid.CreateVersion7(), Scope = scope.Value, CreatedAt = now };
        song.Update(title, null, null, [], null, null, [], now);
        return song;
    }

    public void Update(string title, string? author, string? ccliNumber, IEnumerable<string> themes, string? lyrics, string? referenceUrl, IEnumerable<Arrangement> arrangements, DateTimeOffset now)
    {
        Title = Text.Required(title, 150, "Give the song a title.");
        Author = Text.Optional(author, 200);
        var ccli = Text.Optional(ccliNumber, 12);
        if (ccli is not null && !ccli.All(char.IsAsciiDigit))
        {
            throw new DomainRuleException("services.ccli_invalid", "A CCLI song number is digits only, e.g. 7104200.");
        }

        CcliNumber = ccli;
        Themes = themes.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToList();
        Lyrics = Text.Optional(lyrics, MaxLyrics);
        ReferenceUrl = Link(referenceUrl);
        Arrangements = arrangements.Take(10).Select(a => a with
        {
            Id = a.Id == Guid.Empty ? Guid.CreateVersion7() : a.Id,
            Name = Text.Required(a.Name, 60, "Give each arrangement a name, e.g. Default or Acoustic."),
            Key = Text.Optional(a.Key, 10),
            Bpm = a.Bpm is > 0 and < 400 ? a.Bpm : null,
            ChartUrl = Link(a.ChartUrl),
            ChartFileName = Text.Optional(a.ChartFileName, 200),
            AudioUrl = Link(a.AudioUrl),
            Notes = Text.Optional(a.Notes, 1000),
        }).ToList();
        if (Arrangements.Count == 0)
        {
            Arrangements.Add(new Arrangement(Guid.CreateVersion7(), "Default", null, null, null, null, null, null));
        }

        UpdatedAt = now;
    }

    public void Archive() => IsArchived = true;

    public void Restore() => IsArchived = false;

    private static string? Link(string? value)
    {
        var trimmed = Text.Optional(value, 500);
        return trimmed is null || (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
            ? trimmed
            : throw new DomainRuleException("services.link_invalid", "Links must be web addresses starting with https://.");
    }
}
