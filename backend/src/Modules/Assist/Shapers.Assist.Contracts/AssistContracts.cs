namespace Shapers.Assist.Contracts;

public static class AssistPermissions
{
    /// <summary>Ask for AI drafts (lessons, show notes, rewrites) for content in scope. Drafts are never published by themselves.</summary>
    public const string DraftsCreate = "assist.drafts.create";

    /// <summary>See AI usage, cost and the monthly budget.</summary>
    public const string UsageView = "assist.usage.view";
}

/// <summary>The outcome of turning speech into text.</summary>
public sealed record TranscriptionOutcome(bool Succeeded, string? Text, string? ErrorCode, string? ErrorMessage)
{
    public static TranscriptionOutcome Ok(string text) => new(true, text, null, null);

    public static TranscriptionOutcome Failed(string code, string message) => new(false, null, code, message);
}

/// <summary>
/// Speech to text for other modules (sermon audio). The Assist module keeps the usage log and the monthly budget,
/// so callers never talk to an AI service directly.
/// </summary>
public interface IAssistTranscriber
{
    /// <summary>False when AI help is switched off; callers should not queue work.</summary>
    bool IsEnabled { get; }

    /// <param name="locale">BCP 47 locale of the speech, e.g. "en-ZA".</param>
    /// <param name="purpose">What the text is for, recorded in the usage log, e.g. "sermon-transcript".</param>
    Task<TranscriptionOutcome> TranscribeAsync(Stream audio, string fileName, string contentType, string locale, string purpose, CancellationToken cancellationToken);
}
