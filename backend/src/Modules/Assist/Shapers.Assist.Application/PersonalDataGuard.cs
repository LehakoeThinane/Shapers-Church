using System.Text.RegularExpressions;

namespace Shapers.Assist.Application;

/// <summary>
/// A last line of defence: AI only ever sees public church content, but text pasted by staff could still carry
/// someone's contact details. Email addresses, phone numbers and ID numbers are refused or removed before sending.
/// </summary>
public static partial class PersonalDataGuard
{
    private const string Removed = "[contact details removed]";

    /// <summary>The kinds of personal details found, e.g. "an email address". Empty when the text is clean.</summary>
    public static IReadOnlyList<string> Find(string text)
    {
        var found = new List<string>();
        if (Email().IsMatch(text))
        {
            found.Add("an email address");
        }

        if (Phone().IsMatch(text))
        {
            found.Add("a phone number");
        }

        if (IdNumber().IsMatch(text))
        {
            found.Add("an ID number");
        }

        return found;
    }

    /// <summary>The text with email addresses, phone numbers and ID numbers replaced. Used for sermon transcripts.</summary>
    public static string Redact(string text) =>
        IdNumber().Replace(Phone().Replace(Email().Replace(text, Removed), Removed), Removed);

    /// <summary>
    /// Swaps contact details for placeholders like [[C1]], so published text (e.g. the church office's number on a page)
    /// can be translated without the details being sent. <see cref="Unmask"/> puts them back.
    /// </summary>
    public static string Mask(string text, Dictionary<string, string> masked)
    {
        string Swap(Match m)
        {
            var existing = masked.FirstOrDefault(p => p.Value == m.Value).Key;
            if (existing is not null)
            {
                return existing;
            }

            var token = $"[[C{masked.Count + 1}]]";
            masked[token] = m.Value;
            return token;
        }

        return IdNumber().Replace(Phone().Replace(Email().Replace(text, Swap), Swap), Swap);
    }

    public static string Unmask(string text, IReadOnlyDictionary<string, string> masked) =>
        masked.Aggregate(text, (current, pair) => current.Replace(pair.Key, pair.Value, StringComparison.Ordinal));

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")]
    private static partial Regex Email();

    // South African numbers (0xx xxx xxxx, +27 xx xxx xxxx) and other international numbers with a + prefix.
    [GeneratedRegex(@"(?<!\d)(?:\+\d{1,3}[\s-]?\(?\d{1,3}\)?|0\d{2})[\s-]?\d{3}[\s-]?\d{3,4}(?!\d)")]
    private static partial Regex Phone();

    // South African ID numbers: 13 digits, starting with a plausible date of birth.
    [GeneratedRegex(@"(?<!\d)\d{2}(?:0[1-9]|1[0-2])(?:0[1-9]|[12]\d|3[01])\d{7}(?!\d)")]
    private static partial Regex IdNumber();
}
