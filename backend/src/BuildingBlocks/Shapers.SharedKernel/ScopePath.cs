using System.Text.RegularExpressions;

namespace Shapers.SharedKernel;

public enum ScopeType
{
    Global,
    Campus,
    Ministry,
    Group,
}

/// <summary>
/// Where a record or a permission grant lives in the organisation tree, written as dot-separated labels:
/// <code>
/// shapers                              GLOBAL
/// shapers.campus_rivonia               CAMPUS
/// shapers.campus_rivonia.ministry_kids MINISTRY
/// shapers.ministry_worship             MINISTRY that spans every campus
/// </code>
/// A grant covers a record when the grant's path is the record's path or one of its ancestors.
/// PERSONAL access is not a path; it is an ownership rule applied by each module.
/// </summary>
public sealed partial record ScopePath
{
    private const int MaxLength = 512;

    private ScopePath(string value) => Value = value;

    public string Value { get; }

    public int Depth => Value.Count(c => c == '.') + 1;

    public ScopePath Root => new(Value.Split('.')[0]);

    public ScopePath? Parent
    {
        get
        {
            var lastDot = Value.LastIndexOf('.');
            return lastDot < 0 ? null : new ScopePath(Value[..lastDot]);
        }
    }

    public ScopeType Type
    {
        get
        {
            var label = Value[(Value.LastIndexOf('.') + 1)..];
            if (Depth == 1)
            {
                return ScopeType.Global;
            }

            return label.Split('_')[0] switch
            {
                "campus" => ScopeType.Campus,
                "ministry" => ScopeType.Ministry,
                "group" => ScopeType.Group,
                _ => throw new InvalidOperationException($"Unknown scope label '{label}'."),
            };
        }
    }

    public static ScopePath Parse(string value) =>
        TryParse(value, out var path) ? path : throw new FormatException($"'{value}' is not a valid scope path.");

    public static bool TryParse(string? value, out ScopePath path)
    {
        path = null!;
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxLength || !PathPattern().IsMatch(value))
        {
            return false;
        }

        path = new ScopePath(value);
        return true;
    }

    public static ScopePath Organisation(string slug) => Parse(Label(slug));

    public ScopePath Child(ScopeType type, string slug)
    {
        var prefix = type switch
        {
            ScopeType.Campus => "campus",
            ScopeType.Ministry => "ministry",
            ScopeType.Group => "group",
            _ => throw new ArgumentOutOfRangeException(nameof(type), "Only the root is a global scope."),
        };
        return Parse($"{Value}.{prefix}_{Label(slug)}");
    }

    /// <summary>True when this path is <paramref name="other"/> or one of its ancestors.</summary>
    public bool Covers(ScopePath other) =>
        other.Value == Value || other.Value.StartsWith(Value + ".", StringComparison.Ordinal);

    public override string ToString() => Value;

    /// <summary>Turns a human slug such as "Rivonia Kids" into a path label ("rivonia_kids").</summary>
    public static string Label(string slug)
    {
        var label = NonLabelChars().Replace(slug.Trim().ToLowerInvariant(), "_").Trim('_');
        return label.Length == 0 ? throw new ArgumentException("A scope label needs at least one letter or digit.", nameof(slug)) : label;
    }

    [GeneratedRegex("^[a-z0-9_]+(\\.[a-z0-9_]+)*$")]
    private static partial Regex PathPattern();

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonLabelChars();
}
