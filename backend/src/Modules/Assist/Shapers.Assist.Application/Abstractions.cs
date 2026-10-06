using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Shapers.Assist.Contracts;
using Shapers.Assist.Domain;
using Shapers.Platform.Authorization;

namespace Shapers.Assist.Application;

public interface IAssistDb
{
    DbSet<AiDraft> Drafts { get; }

    DbSet<AiUsage> Usage { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public sealed class AssistPermissionProvider : IPermissionProvider
{
    public IEnumerable<PermissionDefinition> GetPermissions() =>
    [
        new(AssistPermissions.DraftsCreate, "assist", "Ask AI for drafts (cell lessons, sermon notes, rewrites) to review and edit"),
        new(AssistPermissions.UsageView, "assist", "See AI usage, estimated cost and the monthly budget"),
    ];
}

public sealed class AssistOptions
{
    public const string SectionName = "Assist";

    /// <summary>"Disabled" (default: no AI anywhere), "Azure", or "Fake" (canned answers for development and tests).</summary>
    public string Provider { get; set; } = "Disabled";

    /// <summary>Calls stop for the rest of the month once the estimated spend reaches this.</summary>
    public decimal MonthlyBudgetZar { get; set; } = 300;

    public AzureOptions Azure { get; set; } = new();

    public PriceOptions Prices { get; set; } = new();

    public bool IsEnabled => !Provider.Equals("Disabled", StringComparison.OrdinalIgnoreCase);

    public sealed class AzureOptions
    {
        /// <summary>The Azure OpenAI resource, e.g. https://shapers-ai.openai.azure.com.</summary>
        public string? OpenAiEndpoint { get; set; }

        /// <summary>Leave empty in Azure: the API signs in with its managed identity. Only for the demo or local testing.</summary>
        public string? ApiKey { get; set; }

        /// <summary>The deployment name of the writing model.</summary>
        public string ChatDeployment { get; set; } = "gpt-5.4-mini";

        /// <summary>"minimal", "low", "medium" or empty for models without reasoning.</summary>
        public string? ReasoningEffort { get; set; } = "low";

        /// <summary>The Azure AI Speech resource (custom domain), e.g. https://shapers-speech.cognitiveservices.azure.com.</summary>
        public string? SpeechEndpoint { get; set; }

        public string? SpeechKey { get; set; }
    }

    /// <summary>Estimates for the usage page and the budget. Update them when Azure's prices change.</summary>
    public sealed class PriceOptions
    {
        public decimal ChatInputPerMillionTokensZar { get; set; } = 8m;

        public decimal ChatOutputPerMillionTokensZar { get; set; } = 40m;

        public decimal TranscriptionPerHourZar { get; set; } = 7m;
    }
}

/// <summary>A request for structured output: the answer must match <see cref="Schema"/> (JSON Schema, strict).</summary>
public sealed record ChatRequest(string System, string User, string SchemaName, JsonObject Schema, int MaxOutputTokens);

public sealed record ChatResponse(string Json, string Model, int InputTokens, int OutputTokens);

public sealed record TranscriptResponse(string Text, int DurationSeconds, string Model);

/// <summary>A failure the AI service reported, worded for staff.</summary>
public sealed class AiProviderException(string code, string message, Exception? inner = null) : Exception(message, inner)
{
    public string Code { get; } = code;
}

/// <summary>The AI services themselves (Azure OpenAI and Azure AI Speech, or a fake). Only <see cref="AiGateway"/> calls it.</summary>
public interface IAiProvider
{
    string ChatModel { get; }

    Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken cancellationToken);

    Task<TranscriptResponse> TranscribeAsync(Stream audio, string fileName, string contentType, string locale, CancellationToken cancellationToken);
}

public enum RewriteMode
{
    Tidy,
    Shorter,
    Longer,
}

public sealed record SermonDraftRequest(Guid SermonId);

public sealed record RewriteRequest(string Text, RewriteMode Mode);

public sealed record AssistStatusDto(bool Enabled, bool CanDraft, decimal BudgetZar, decimal SpentThisMonthZar, bool BudgetReached);

public sealed record LessonDraft(
    string Title,
    string Summary,
    IReadOnlyList<string> KeyVerses,
    string Icebreaker,
    IReadOnlyList<string> Questions,
    string Application,
    string PrayerFocus,
    string Body);

public sealed record NotesDraft(string Summary, string Notes, IReadOnlyList<string> Topics);

public sealed record RewriteDraft(string Text);

/// <summary>A draft with its content in the shape of its kind. Exactly one of Lesson, Notes or Rewrite is set.</summary>
public sealed record DraftDto(
    Guid Id,
    DraftKind Kind,
    string SourceType,
    Guid? SourceId,
    DraftStatus Status,
    string Model,
    string PromptVersion,
    DateTimeOffset RequestedAt,
    bool RequestedByMe,
    LessonDraft? Lesson,
    NotesDraft? Notes,
    RewriteDraft? Rewrite);

public sealed record UsageMonthDto(string Month, int Calls, decimal CostZar);

public sealed record UsageCallDto(DateTimeOffset OccurredAt, AiOperation Operation, string Purpose, string Model, int InputTokens, int OutputTokens, int AudioSeconds, decimal CostZar, bool Succeeded);

public sealed record UsageDto(
    bool Enabled,
    string Provider,
    string ChatModel,
    decimal BudgetZar,
    decimal SpentThisMonthZar,
    IReadOnlyList<UsageMonthDto> Months,
    IReadOnlyList<UsageCallDto> Recent);
