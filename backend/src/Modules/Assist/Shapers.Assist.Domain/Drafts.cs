namespace Shapers.Assist.Domain;

public enum DraftKind
{
    /// <summary>A cell lesson from a sermon: summary, verses, discussion questions.</summary>
    SermonLesson,

    /// <summary>A sermon's summary, notes and topics for the app and website.</summary>
    SermonNotes,

    /// <summary>Someone's own text, tidied up, shortened or lengthened.</summary>
    Rewrite,

    /// <summary>A page or post in another language, for a speaker of it to check.</summary>
    Translation,
}

public enum DraftStatus
{
    Pending,
    Accepted,
    Discarded,
}

/// <summary>
/// Something AI wrote for a person to review. It is never published from here: the person copies it into the
/// real editor, changes what they want, saves it there, and marks the draft accepted. Until then it is only a suggestion.
/// </summary>
public sealed class AiDraft : AggregateRoot<Guid>
{
    /// <summary>Drafts are working material, not records: they are deleted after this long.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(90);

    private AiDraft()
    {
    }

    public DraftKind Kind { get; private set; }

    /// <summary>What it was written from, e.g. "sermon", or "text" for a rewrite.</summary>
    public string SourceType { get; private set; } = null!;

    public Guid? SourceId { get; private set; }

    /// <summary>The scope of the source; only people allowed to draft there can see it.</summary>
    public string Scope { get; private set; } = null!;

    public string Model { get; private set; } = null!;

    /// <summary>Which prompt file produced it, e.g. "sermon-lesson.v1", so changes to prompts can be traced.</summary>
    public string PromptVersion { get; private set; } = null!;

    /// <summary>The model's answer as JSON, shaped by the kind's schema.</summary>
    public string Output { get; private set; } = null!;

    public DraftStatus Status { get; private set; }

    public Guid RequestedByUserId { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    public Guid? ReviewedByUserId { get; private set; }

    public DateTimeOffset? ReviewedAt { get; private set; }

    public static AiDraft Create(DraftKind kind, string sourceType, Guid? sourceId, ScopePath scope, string model, string promptVersion, string output, Guid requestedBy, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            throw new DomainRuleException("assist.empty_output", "The AI service returned nothing. Try again.");
        }

        return new AiDraft
        {
            Id = Guid.CreateVersion7(),
            Kind = kind,
            SourceType = sourceType,
            SourceId = sourceId,
            Scope = scope.Value,
            Model = model,
            PromptVersion = promptVersion,
            Output = output,
            Status = DraftStatus.Pending,
            RequestedByUserId = requestedBy,
            RequestedAt = now,
        };
    }

    /// <summary>A person has used the draft (usually after editing it) in the real editor.</summary>
    public void Accept(Guid userId, DateTimeOffset now) => Review(DraftStatus.Accepted, userId, now);

    public void Discard(Guid userId, DateTimeOffset now) => Review(DraftStatus.Discarded, userId, now);

    private void Review(DraftStatus status, Guid userId, DateTimeOffset now)
    {
        if (Status == status)
        {
            return;
        }

        if (Status != DraftStatus.Pending)
        {
            throw new DomainRuleException("assist.draft_reviewed", "This draft has already been dealt with.");
        }

        Status = status;
        ReviewedByUserId = userId;
        ReviewedAt = now;
    }
}

public enum AiOperation
{
    Chat,
    Transcription,
    Embedding,
}

/// <summary>One call to an AI service, for the usage page and the monthly budget. Holds no content.</summary>
public sealed class AiUsage
{
    private AiUsage()
    {
    }

    public Guid Id { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public AiOperation Operation { get; private set; }

    /// <summary>What it was for, e.g. "sermon-lesson" or "sermon-transcript".</summary>
    public string Purpose { get; private set; } = null!;

    public string Model { get; private set; } = null!;

    public int InputTokens { get; private set; }

    public int OutputTokens { get; private set; }

    public int AudioSeconds { get; private set; }

    /// <summary>Estimated from the configured prices; the Azure invoice is the real figure.</summary>
    public decimal CostZar { get; private set; }

    public Guid? UserId { get; private set; }

    public bool Succeeded { get; private set; }

    public static AiUsage Record(AiOperation operation, string purpose, string model, int inputTokens, int outputTokens, int audioSeconds, decimal costZar, Guid? userId, bool succeeded, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            OccurredAt = now,
            Operation = operation,
            Purpose = purpose,
            Model = model,
            InputTokens = Math.Max(0, inputTokens),
            OutputTokens = Math.Max(0, outputTokens),
            AudioSeconds = Math.Max(0, audioSeconds),
            CostZar = Math.Max(0, Math.Round(costZar, 4)),
            UserId = userId,
            Succeeded = succeeded,
        };
}
