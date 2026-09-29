using PhoneNumbers;

namespace Shapers.Platform.Text;

/// <summary>
/// One normal form for contact details everywhere, so matching and deduplication work:
/// phones as E.164 (+27821234567), emails trimmed and lower-case.
/// </summary>
public static class ContactNormaliser
{
    public const string DefaultRegion = "ZA";

    private static readonly PhoneNumberUtil Phones = PhoneNumberUtil.GetInstance();

    /// <summary>Accepts local ("082 123 4567") or international ("+27 82 123 4567") input.</summary>
    public static bool TryNormalisePhone(string? input, out string e164, string defaultRegion = DefaultRegion)
    {
        e164 = string.Empty;
        if (string.IsNullOrWhiteSpace(input) || input.Length > 32)
        {
            return false;
        }

        try
        {
            var number = Phones.Parse(input, defaultRegion);
            if (!Phones.IsValidNumber(number))
            {
                return false;
            }

            e164 = Phones.Format(number, PhoneNumberFormat.E164);
            return true;
        }
        catch (NumberParseException)
        {
            return false;
        }
    }

    public static bool TryNormaliseEmail(string? input, out string email)
    {
        email = input?.Trim().ToLowerInvariant() ?? string.Empty;
        var at = email.IndexOf('@', StringComparison.Ordinal);
        return email.Length is > 2 and <= 254
            && at > 0
            && at == email.LastIndexOf('@')
            && email.IndexOf('.', at) > at + 1
            && !email.Any(char.IsWhiteSpace);
    }

    /// <summary>For logs and screens: +27 82 *** 4567.</summary>
    public static string MaskPhone(string e164) =>
        e164.Length < 8 ? "***" : $"{e164[..5]} *** {e164[^4..]}";
}
