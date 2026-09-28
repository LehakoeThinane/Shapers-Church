namespace Shapers.Platform.Text;

public static class LikePattern
{
    /// <summary>Escapes LIKE/ILIKE wildcards so user input is matched literally (Postgres' default escape is backslash).</summary>
    public static string Escape(string value) =>
        value.Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);

    public static string Contains(string value) => $"%{Escape(value)}%";
}
