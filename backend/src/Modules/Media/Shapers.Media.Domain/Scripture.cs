using System.Globalization;
using System.Text.RegularExpressions;

namespace Shapers.Media.Domain;

/// <summary>A book of the 66-book Protestant canon, with the names people actually type.</summary>
public sealed record BibleBook(int Number, string Name, int Chapters, string[] Aliases)
{
    /// <summary>"Psalm 23" reads better than "Psalms 23" for a single chapter.</summary>
    public string DisplayName(bool singleChapter) => Number == 19 && singleChapter ? "Psalm" : Name;
}

public static partial class Bible
{
    public static readonly IReadOnlyList<BibleBook> Books =
    [
        new(1, "Genesis", 50, ["gen", "ge", "gn"]),
        new(2, "Exodus", 40, ["exod", "exo", "ex"]),
        new(3, "Leviticus", 27, ["lev", "le", "lv"]),
        new(4, "Numbers", 36, ["num", "nu", "nm"]),
        new(5, "Deuteronomy", 34, ["deut", "deu", "dt"]),
        new(6, "Joshua", 24, ["josh", "jos"]),
        new(7, "Judges", 21, ["judg", "jdg"]),
        new(8, "Ruth", 4, ["ru", "rth"]),
        new(9, "1 Samuel", 31, ["1sam", "1sa", "1sm"]),
        new(10, "2 Samuel", 24, ["2sam", "2sa", "2sm"]),
        new(11, "1 Kings", 22, ["1kgs", "1ki", "1kg"]),
        new(12, "2 Kings", 25, ["2kgs", "2ki", "2kg"]),
        new(13, "1 Chronicles", 29, ["1chr", "1ch", "1chron"]),
        new(14, "2 Chronicles", 36, ["2chr", "2ch", "2chron"]),
        new(15, "Ezra", 10, ["ezr"]),
        new(16, "Nehemiah", 13, ["neh", "ne"]),
        new(17, "Esther", 10, ["esth", "est"]),
        new(18, "Job", 42, ["jb"]),
        new(19, "Psalms", 150, ["psalm", "ps", "psa", "pss", "psm"]),
        new(20, "Proverbs", 31, ["prov", "pro", "prv", "pr"]),
        new(21, "Ecclesiastes", 12, ["eccl", "ecc", "ec", "qoh"]),
        new(22, "Song of Songs", 8, ["songofsolomon", "song", "sos", "songofsongs", "canticles"]),
        new(23, "Isaiah", 66, ["isa", "is"]),
        new(24, "Jeremiah", 52, ["jer", "je"]),
        new(25, "Lamentations", 5, ["lam", "la"]),
        new(26, "Ezekiel", 48, ["ezek", "eze", "ezk"]),
        new(27, "Daniel", 12, ["dan", "da", "dn"]),
        new(28, "Hosea", 14, ["hos", "ho"]),
        new(29, "Joel", 3, ["jl"]),
        new(30, "Amos", 9, ["am"]),
        new(31, "Obadiah", 1, ["obad", "ob"]),
        new(32, "Jonah", 4, ["jon", "jnh"]),
        new(33, "Micah", 7, ["mic", "mc"]),
        new(34, "Nahum", 3, ["nah", "na"]),
        new(35, "Habakkuk", 3, ["hab", "hb"]),
        new(36, "Zephaniah", 3, ["zeph", "zep", "zp"]),
        new(37, "Haggai", 2, ["hag", "hg"]),
        new(38, "Zechariah", 14, ["zech", "zec", "zc"]),
        new(39, "Malachi", 4, ["mal", "ml"]),
        new(40, "Matthew", 28, ["matt", "mat", "mt"]),
        new(41, "Mark", 16, ["mk", "mrk", "mar"]),
        new(42, "Luke", 24, ["lk", "luk"]),
        new(43, "John", 21, ["jn", "jhn", "joh"]),
        new(44, "Acts", 28, ["ac", "act"]),
        new(45, "Romans", 16, ["rom", "ro", "rm"]),
        new(46, "1 Corinthians", 16, ["1cor", "1co"]),
        new(47, "2 Corinthians", 13, ["2cor", "2co"]),
        new(48, "Galatians", 6, ["gal", "ga"]),
        new(49, "Ephesians", 6, ["eph", "ep"]),
        new(50, "Philippians", 4, ["phil", "php", "pp"]),
        new(51, "Colossians", 4, ["col", "co"]),
        new(52, "1 Thessalonians", 5, ["1thess", "1th", "1thes"]),
        new(53, "2 Thessalonians", 3, ["2thess", "2th", "2thes"]),
        new(54, "1 Timothy", 6, ["1tim", "1ti", "1tm"]),
        new(55, "2 Timothy", 4, ["2tim", "2ti", "2tm"]),
        new(56, "Titus", 3, ["tit", "ti"]),
        new(57, "Philemon", 1, ["philem", "phm", "phlm"]),
        new(58, "Hebrews", 13, ["heb"]),
        new(59, "James", 5, ["jas", "jm"]),
        new(60, "1 Peter", 5, ["1pet", "1pe", "1pt"]),
        new(61, "2 Peter", 3, ["2pet", "2pe", "2pt"]),
        new(62, "1 John", 5, ["1jn", "1jhn", "1joh"]),
        new(63, "2 John", 1, ["2jn", "2jhn", "2joh"]),
        new(64, "3 John", 1, ["3jn", "3jhn", "3joh"]),
        new(65, "Jude", 1, ["jud", "jd"]),
        new(66, "Revelation", 22, ["rev", "re", "revelations", "apocalypse"]),
    ];

    private static readonly Dictionary<string, BibleBook> Lookup = BuildLookup();

    public static BibleBook Book(int number) => Books[number - 1];

    /// <summary>Finds a book from free text: "1 Cor", "I Corinthians", "First Corinthians", "Ps", "Psalm".</summary>
    public static BibleBook? FindBook(string text) => Lookup.GetValueOrDefault(NormaliseBookName(text));

    internal static string NormaliseBookName(string text)
    {
        var s = text.Trim().ToLowerInvariant().Replace(".", string.Empty, StringComparison.Ordinal);
        s = OrdinalPrefix().Replace(s, m => m.Groups[1].Value switch
        {
            "iii" or "third" or "3rd" => "3",
            "ii" or "second" or "2nd" => "2",
            _ => "1",
        });
        return Whitespace().Replace(s, string.Empty);
    }

    private static Dictionary<string, BibleBook> BuildLookup()
    {
        var lookup = new Dictionary<string, BibleBook>(StringComparer.Ordinal);
        foreach (var book in Books)
        {
            lookup[NormaliseBookName(book.Name)] = book;
            foreach (var alias in book.Aliases)
            {
                lookup.TryAdd(alias, book);
            }
        }

        return lookup;
    }

    [GeneratedRegex(@"^(iii|ii|i|first|second|third|1st|2nd|3rd)\s+(?=[a-z])")]
    private static partial Regex OrdinalPrefix();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}

/// <summary>
/// A passage such as Psalm 42:1–11, John 3, or Isaiah 52:13–53:12. Verses are optional; a missing
/// verse means "the whole chapter".
/// </summary>
public sealed partial record ScriptureReference(int BookNumber, int ChapterFrom, int? VerseFrom, int ChapterTo, int? VerseTo)
{
    public BibleBook Book => Bible.Book(BookNumber);

    public override string ToString()
    {
        var singleChapter = ChapterFrom == ChapterTo;
        var name = Book.DisplayName(singleChapter);
        var text = $"{name} {ChapterFrom}";
        if (VerseFrom is { } vf)
        {
            text += $":{vf}";
        }

        if (!singleChapter)
        {
            text += VerseTo is { } vt ? $"–{ChapterTo}:{vt}" : $"–{ChapterTo}";
        }
        else if (VerseTo is { } vt && vt != VerseFrom)
        {
            text += $"–{vt}";
        }

        return text;
    }

    /// <summary>Parses one reference. Returns false for anything that isn't a real book, chapter and verse range.</summary>
    public static bool TryParse(string? input, out ScriptureReference reference)
    {
        reference = null!;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var match = Pattern().Match(input.Trim());
        if (!match.Success || Bible.FindBook(match.Groups["book"].Value) is not { } book)
        {
            return false;
        }

        var c1 = int.Parse(match.Groups["c1"].Value, CultureInfo.InvariantCulture);
        int? v1 = match.Groups["v1"].Success ? int.Parse(match.Groups["v1"].Value, CultureInfo.InvariantCulture) : null;
        var c2 = c1;
        int? v2 = v1;

        if (match.Groups["e1"].Success)
        {
            var first = int.Parse(match.Groups["e1"].Value, CultureInfo.InvariantCulture);
            if (match.Groups["e2"].Success)
            {
                // "52:13-53:12": chapter and verse on both ends.
                c2 = first;
                v2 = int.Parse(match.Groups["e2"].Value, CultureInfo.InvariantCulture);
            }
            else if (v1 is null)
            {
                // "Genesis 1-3": a chapter range.
                c2 = first;
            }
            else
            {
                // "42:1-11": a verse range within one chapter.
                v2 = first;
            }
        }

        // Single-chapter books: "Jude 3" means verse 3, and "Jude 3-5" verses 3 to 5.
        if (book.Chapters == 1 && v1 is null && c1 > 1)
        {
            (v1, v2, c1, c2) = (c1, c2, 1, 1);
        }

        if (c1 < 1 || c2 < c1 || c2 > book.Chapters || v1 is < 1 || v2 is < 1 || (c1 == c2 && v1 is { } a && v2 is { } b && b < a))
        {
            return false;
        }

        reference = new ScriptureReference(book.Number, c1, v1, c2, v2);
        return true;
    }

    /// <summary>Parses a list separated by semicolons: "Isaiah 42:1-9; Matthew 12:18-21".</summary>
    public static IReadOnlyList<ScriptureReference> ParseMany(string? input, out IReadOnlyList<string> unrecognised)
    {
        var references = new List<ScriptureReference>();
        var bad = new List<string>();
        foreach (var part in (input ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (TryParse(part, out var reference))
            {
                references.Add(reference);
            }
            else
            {
                bad.Add(part);
            }
        }

        unrecognised = bad;
        return references;
    }

    /// <summary>
    /// Pulls a reference off the front of a title: "Psalm 42: 1-11 Deep calls unto deep" gives
    /// (Psalm 42:1–11, "Deep calls unto deep"). Used when importing existing sermons.
    /// </summary>
    public static bool TryExtractFromTitle(string title, out ScriptureReference reference, out string remainder)
    {
        reference = null!;
        remainder = title.Trim();
        var match = LeadingReference().Match(remainder);
        if (!match.Success || !TryParse(match.Groups["ref"].Value, out reference))
        {
            return false;
        }

        remainder = match.Groups["rest"].Value.Trim().TrimStart('-', '–', '—', '|', ':', ' ');
        return remainder.Length > 0;
    }

    private const string ReferenceBody =
        @"(?<book>(?:[1-3]|i{1,3}|first|second|third)?\s*[A-Za-z][A-Za-z\. ]*?)\s*(?<c1>\d{1,3})(?:\s*[:\.]\s*(?<v1>\d{1,3}))?(?:\s*[-–—]\s*(?<e1>\d{1,3})(?:\s*[:\.]\s*(?<e2>\d{1,3}))?)?";

    [GeneratedRegex("^" + ReferenceBody + "$", RegexOptions.IgnoreCase)]
    private static partial Regex Pattern();

    [GeneratedRegex(@"^(?<ref>(?:[1-3]\s*)?[A-Za-z]+\.?\s*\d{1,3}(?:\s*[:\.]\s*\d{1,3})?(?:\s*[-–—]\s*\d{1,3}(?:\s*:\s*\d{1,3})?)?)(?<rest>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingReference();
}
