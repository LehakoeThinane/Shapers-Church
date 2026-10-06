using System.Collections.Concurrent;
using System.Text.Json.Nodes;

namespace Shapers.Assist.Application;

/// <summary>A prompt file: the instructions (system) and the request template (user), split by "=== user ===".</summary>
public sealed record Prompt(string Version, string System, string UserTemplate)
{
    /// <summary>Fills {{name}} placeholders. Values are inserted as plain text.</summary>
    public string User(IReadOnlyDictionary<string, string> values) =>
        values.Aggregate(UserTemplate, (text, pair) => text.Replace("{{" + pair.Key + "}}", pair.Value, StringComparison.Ordinal));
}

/// <summary>Prompts are versioned files embedded in this assembly, so changing one is reviewed like code.</summary>
public static class Prompts
{
    public const string SermonLesson = "sermon-lesson.v1";
    public const string SermonNotes = "sermon-notes.v1";
    public const string Rewrite = "rewrite.v1";

    private const string Separator = "=== user ===";
    private static readonly ConcurrentDictionary<string, Prompt> Cache = new();

    public static Prompt Get(string version) => Cache.GetOrAdd(version, Load);

    private static Prompt Load(string version)
    {
        using var stream = typeof(Prompts).Assembly.GetManifestResourceStream($"Prompts.{version}.md")
            ?? throw new InvalidOperationException($"Prompt '{version}' is missing.");
        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);
        var split = text.IndexOf(Separator, StringComparison.Ordinal);
        if (split < 0)
        {
            throw new InvalidOperationException($"Prompt '{version}' has no '{Separator}' line.");
        }

        return new Prompt(version, text[..split].Trim(), text[(split + Separator.Length)..].Trim());
    }
}

/// <summary>JSON Schemas for structured output. Strict mode: every property required, nothing extra.</summary>
public static class DraftSchemas
{
    public static JsonObject Lesson() => Object(
        ("title", Text()),
        ("summary", Text()),
        ("keyVerses", TextList()),
        ("icebreaker", Text()),
        ("questions", TextList()),
        ("application", Text()),
        ("prayerFocus", Text()));

    public static JsonObject Notes() => Object(
        ("summary", Text()),
        ("notes", Text()),
        ("topics", TextList()));

    public static JsonObject Rewrite() => Object(("text", Text()));

    private static JsonObject Text() => new() { ["type"] = "string" };

    private static JsonObject TextList() => new() { ["type"] = "array", ["items"] = Text() };

    private static JsonObject Object(params (string Name, JsonObject Schema)[] properties)
    {
        var props = new JsonObject();
        var required = new JsonArray();
        foreach (var (name, schema) in properties)
        {
            props[name] = schema;
            required.Add(name);
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = props,
            ["required"] = required,
            ["additionalProperties"] = false,
        };
    }
}
