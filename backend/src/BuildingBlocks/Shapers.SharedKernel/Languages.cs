namespace Shapers.SharedKernel;

/// <summary>A language content can be written in, by its ISO 639-1 code (e.g. "zu").</summary>
public sealed record Language(string Code, string Name);

/// <summary>
/// Languages the platform knows: English (the original of everything) and South African languages for translations.
/// Settings choose which ones are offered; the list here only says which codes are valid.
/// </summary>
public static class Languages
{
    public const string English = "en";

    public static readonly IReadOnlyList<Language> All =
    [
        new(English, "English"),
        new("zu", "isiZulu"),
        new("st", "Sesotho"),
        new("xh", "isiXhosa"),
        new("af", "Afrikaans"),
        new("tn", "Setswana"),
        new("nso", "Sepedi"),
        new("ts", "Xitsonga"),
        new("ve", "Tshivenda"),
        new("ss", "siSwati"),
        new("nr", "isiNdebele"),
    ];

    public static bool IsKnown(string? code) => code is not null && All.Any(l => l.Code == code);

    public static string NameOf(string code) => All.FirstOrDefault(l => l.Code == code)?.Name ?? code;
}
