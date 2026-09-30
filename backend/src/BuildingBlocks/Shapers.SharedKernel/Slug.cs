using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Shapers.SharedKernel;

/// <summary>Readable, URL-safe names: "Anointed to grow!" gives "anointed-to-grow".</summary>
public static partial class Slug
{
    public const int MaxLength = 80;

    public static string From(string text, string fallback)
    {
        // Strip accents (é -> e) before dropping anything that isn't a letter or digit.
        var decomposed = (text ?? string.Empty).ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var plain = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark))
        {
            plain.Append(c);
        }

        var words = MultiDash().Replace(NonSlug().Replace(plain.ToString(), "-"), "-").Trim('-');
        if (words.Length > MaxLength)
        {
            words = words[..MaxLength].TrimEnd('-');
        }

        return words.Length == 0 ? fallback : words;
    }

    public static bool IsValid(string? slug) => slug is { Length: > 0 and <= MaxLength } && Valid().IsMatch(slug);

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonSlug();

    [GeneratedRegex("-{2,}")]
    private static partial Regex MultiDash();

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex Valid();
}
