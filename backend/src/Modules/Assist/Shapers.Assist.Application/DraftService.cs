using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shapers.Assist.Contracts;
using Shapers.Assist.Domain;
using Shapers.Content.Contracts;
using Shapers.Media.Contracts;
using Shapers.Platform.Auditing;
using Shapers.Platform.Authorization;

namespace Shapers.Assist.Application;

/// <summary>
/// Asks AI for drafts and keeps them for review. Sources are read through other modules' contracts, which only
/// expose public church content (a sermon's own words), never anything about people.
/// </summary>
public sealed class DraftService(
    AiGateway gateway,
    IAssistDb db,
    ISermonSource sermons,
    IContentSource content,
    IAuthorizer authorizer,
    ICurrentUser currentUser,
    IAuditLog audit,
    IOptions<AssistOptions> options,
    TimeProvider clock)
{
    /// <summary>Roughly three hours of speech; longer transcripts are cut, keeping the start.</summary>
    public const int MaxTranscriptLength = 150_000;

    public const int MaxRewriteLength = 20_000;

    private static readonly Error NotFound = Error.NotFound("assist.draft_not_found", "Draft not found.");
    private static readonly Error SermonNotFound = Error.NotFound("assist.sermon_not_found", "Sermon not found.");
    private static readonly CultureInfo SouthAfrica = CultureInfo.GetCultureInfo("en-ZA");

    public async Task<AssistStatusDto> StatusAsync(CancellationToken cancellationToken)
    {
        var spent = gateway.IsEnabled ? await gateway.SpentThisMonthAsync(cancellationToken) : 0;
        var budget = options.Value.MonthlyBudgetZar;
        var canDraft = gateway.IsEnabled && await authorizer.HasAnywhereAsync(AssistPermissions.DraftsCreate, cancellationToken);
        return new AssistStatusDto(gateway.IsEnabled, canDraft, budget, Math.Round(spent, 2), spent >= budget, TranslationLanguages());
    }

    /// <summary>The languages offered for translation drafts, from settings.</summary>
    public IReadOnlyList<LanguageOptionDto> TranslationLanguages() =>
        options.Value.Languages.Where(c => c != Languages.English && Languages.IsKnown(c)).Distinct().Select(c => new LanguageOptionDto(c, Languages.NameOf(c))).ToList();

    public const int MaxTranslationLength = 20_000;

    /// <summary>
    /// A page or post in another language. Contact details are swapped for placeholders before sending and put back after,
    /// so they're kept without being sent. A fluent speaker checks the result before it's published (Content enforces that).
    /// </summary>
    public async Task<Result<DraftDto>> TranslateAsync(TranslateRequest request, CancellationToken cancellationToken)
    {
        if (!TranslationLanguages().Any(l => l.Code == request.Language))
        {
            return new Error("assist.language_not_offered", "That language isn't offered for translation yet.");
        }

        var source = await content.GetForTranslationAsync(request.SourceType, request.SourceId, cancellationToken);
        if (source is null || !await CanAsync(source.Scope, cancellationToken))
        {
            return Error.NotFound("assist.source_not_found", "Page or post not found.");
        }

        if (source.Body.Length > MaxTranslationLength)
        {
            return new Error("assist.too_long", $"This is too long to translate in one go ({MaxTranslationLength:N0} characters at most). Split it into shorter pages.");
        }

        var masked = new Dictionary<string, string>();
        var prompt = Prompts.Get(Prompts.Translate);
        var user = prompt.User(new Dictionary<string, string>
        {
            ["language"] = Languages.NameOf(request.Language),
            ["title"] = PersonalDataGuard.Mask(source.Title, masked),
            ["summary"] = PersonalDataGuard.Mask(source.Summary ?? string.Empty, masked),
            ["body"] = PersonalDataGuard.Mask(source.Body, masked),
        });
        var response = await gateway.ChatAsync("translate", new ChatRequest(prompt.System, user, "translation", DraftSchemas.Translation(), 12_000 + source.Body.Length), cancellationToken);
        if (response.IsFailure)
        {
            return response.Error!;
        }

        TranslationDraft translated;
        try
        {
            translated = DraftReader.Translation(response.Value.Json, request.Language);
        }
        catch (JsonException)
        {
            return new Error("assist.bad_output", "The AI service gave an answer we couldn't read. Try again.");
        }

        var all = string.Concat(translated.Title, translated.Summary, translated.Body);
        if (masked.Keys.Any(token => !all.Contains(token, StringComparison.Ordinal)))
        {
            return new Error("assist.lost_details", "The translation dropped some contact details. Try again.");
        }

        // Stored with the contact details back in place, ready to become the translation.
        var restored = translated with
        {
            Title = PersonalDataGuard.Unmask(translated.Title, masked),
            Summary = PersonalDataGuard.Unmask(translated.Summary, masked),
            Body = PersonalDataGuard.Unmask(translated.Body, masked),
        };
        return await SaveAsync(
            DraftKind.Translation,
            request.SourceType.ToString().ToLowerInvariant(),
            source.Id,
            ScopePath.Parse(source.Scope),
            prompt.Version,
            response.Value with { Json = JsonSerializer.Serialize(restored, DraftReader.JsonOptions) },
            cancellationToken);
    }

    public Task<Result<DraftDto>> SermonLessonAsync(SermonDraftRequest request, CancellationToken cancellationToken) =>
        FromSermonAsync(request.SermonId, DraftKind.SermonLesson, Prompts.SermonLesson, "sermon_lesson", DraftSchemas.Lesson(), 8000, cancellationToken);

    public Task<Result<DraftDto>> SermonNotesAsync(SermonDraftRequest request, CancellationToken cancellationToken) =>
        FromSermonAsync(request.SermonId, DraftKind.SermonNotes, Prompts.SermonNotes, "sermon_notes", DraftSchemas.Notes(), 8000, cancellationToken);

    public async Task<Result<DraftDto>> RewriteAsync(RewriteRequest request, CancellationToken cancellationToken)
    {
        var text = request.Text?.Trim() ?? string.Empty;
        if (text.Length is 0 or > MaxRewriteLength)
        {
            return new Error("assist.text_invalid", $"Give some text to work on ({MaxRewriteLength:N0} characters at most).");
        }

        var personal = PersonalDataGuard.Find(text);
        if (personal.Count > 0)
        {
            return new Error("assist.personal_data", $"Remove {string.Join(" and ", personal)} first. AI help is only for text meant for everyone.");
        }

        var scopes = await authorizer.ScopesForAsync(AssistPermissions.DraftsCreate, cancellationToken);
        if (scopes.Count == 0)
        {
            return Error.Forbidden("assist.forbidden", "You can't use AI help.");
        }

        var instruction = request.Mode switch
        {
            RewriteMode.Shorter => "Make this text about half as long, keeping the essential information.",
            RewriteMode.Longer => "Make this text about one and a half times as long, explaining what is already there more fully. Add no new facts.",
            _ => "Correct the spelling, grammar and punctuation, and make this text clearer and easier to read. Keep it about the same length.",
        };
        var prompt = Prompts.Get(Prompts.Rewrite);
        var user = prompt.User(new Dictionary<string, string> { ["instruction"] = instruction, ["text"] = text });
        var response = await gateway.ChatAsync("rewrite", new ChatRequest(prompt.System, user, "rewrite", DraftSchemas.Rewrite(), 4000 + text.Length), cancellationToken);
        if (response.IsFailure)
        {
            return response.Error!;
        }

        return await SaveAsync(DraftKind.Rewrite, "text", null, ScopeSet.Collapse(scopes)[0], prompt.Version, response.Value, cancellationToken);
    }

    public async Task<Result<DraftDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var draft = await db.Drafts.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
        return draft is null || !await CanAsync(draft.Scope, cancellationToken) ? NotFound : ToDto(draft);
    }

    /// <summary>Drafts made from one source (e.g. a sermon), newest first.</summary>
    public async Task<IReadOnlyList<DraftDto>> ForSourceAsync(string sourceType, Guid sourceId, CancellationToken cancellationToken)
    {
        var scopes = await authorizer.ScopesForAsync(AssistPermissions.DraftsCreate, cancellationToken);
        var drafts = await db.Drafts.AsNoTracking()
            .Where(d => d.SourceType == sourceType && d.SourceId == sourceId)
            .WithinScopes(d => d.Scope, scopes)
            .OrderByDescending(d => d.RequestedAt)
            .Take(20)
            .ToListAsync(cancellationToken);
        return drafts.Select(ToDto).ToList();
    }

    public Task<Result<DraftDto>> AcceptAsync(Guid id, CancellationToken cancellationToken) =>
        ReviewAsync(id, "assist.draft.accepted", (d, user, now) => d.Accept(user, now), cancellationToken);

    public Task<Result<DraftDto>> DiscardAsync(Guid id, CancellationToken cancellationToken) =>
        ReviewAsync(id, "assist.draft.discarded", (d, user, now) => d.Discard(user, now), cancellationToken);

    private async Task<Result<DraftDto>> FromSermonAsync(
        Guid sermonId,
        DraftKind kind,
        string promptVersion,
        string schemaName,
        System.Text.Json.Nodes.JsonObject schema,
        int maxOutputTokens,
        CancellationToken cancellationToken)
    {
        var sermon = await sermons.GetForDraftingAsync(sermonId, cancellationToken);
        if (sermon is null || !await CanAsync(sermon.Scope, cancellationToken))
        {
            return SermonNotFound;
        }

        if (string.IsNullOrWhiteSpace(sermon.Transcript))
        {
            return new Error("assist.no_transcript", "This sermon has no transcript yet. Transcribe its audio or paste the transcript first.");
        }

        var transcript = sermon.Transcript.Length > MaxTranscriptLength ? sermon.Transcript[..MaxTranscriptLength] : sermon.Transcript;
        var prompt = Prompts.Get(promptVersion);
        var user = prompt.User(new Dictionary<string, string>
        {
            ["title"] = sermon.Title,
            ["date"] = sermon.PreachedOn.ToString("d MMMM yyyy", SouthAfrica),
            ["speakers"] = sermon.Speakers.Count > 0 ? string.Join(", ", sermon.Speakers) : "Not recorded",
            ["scripture"] = sermon.Scripture.Count > 0 ? string.Join("; ", sermon.Scripture) : "Not recorded",
            ["summary"] = sermon.Summary ?? "None",
            ["transcript"] = PersonalDataGuard.Redact(transcript),
        });

        var response = await gateway.ChatAsync(promptVersion.Split('.')[0], new ChatRequest(prompt.System, user, schemaName, schema, maxOutputTokens), cancellationToken);
        if (response.IsFailure)
        {
            return response.Error!;
        }

        return await SaveAsync(kind, "sermon", sermon.Id, ScopePath.Parse(sermon.Scope), promptVersion, response.Value, cancellationToken);
    }

    private async Task<Result<DraftDto>> SaveAsync(DraftKind kind, string sourceType, Guid? sourceId, ScopePath scope, string promptVersion, ChatResponse response, CancellationToken cancellationToken)
    {
        if (!DraftReader.IsValid(kind, response.Json))
        {
            return new Error("assist.bad_output", "The AI service gave an answer we couldn't read. Try again.");
        }

        var draft = AiDraft.Create(kind, sourceType, sourceId, scope, response.Model, promptVersion, response.Json, currentUser.UserId!.Value, clock.GetUtcNow());
        db.Drafts.Add(draft);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord("assist.draft.created", "ai_draft", draft.Id.ToString(), scope, new { Kind = kind.ToString(), sourceType, sourceId, draft.Model, promptVersion }), cancellationToken);
        return ToDto(draft);
    }

    private async Task<Result<DraftDto>> ReviewAsync(Guid id, string action, Action<AiDraft, Guid, DateTimeOffset> review, CancellationToken cancellationToken)
    {
        var draft = await db.Drafts.SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (draft is null || !await CanAsync(draft.Scope, cancellationToken))
        {
            return NotFound;
        }

        review(draft, currentUser.UserId!.Value, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord(action, "ai_draft", draft.Id.ToString(), ScopePath.Parse(draft.Scope), new { Kind = draft.Kind.ToString() }), cancellationToken);
        return ToDto(draft);
    }

    private Task<bool> CanAsync(string scope, CancellationToken cancellationToken) =>
        authorizer.CanAsync(AssistPermissions.DraftsCreate, ScopePath.Parse(scope), cancellationToken);

    private DraftDto ToDto(AiDraft d) => new(
        d.Id,
        d.Kind,
        d.SourceType,
        d.SourceId,
        d.Status,
        d.Model,
        d.PromptVersion,
        d.RequestedAt,
        d.RequestedByUserId == currentUser.UserId,
        d.Kind == DraftKind.SermonLesson ? DraftReader.Lesson(d.Output) : null,
        d.Kind == DraftKind.SermonNotes ? DraftReader.Notes(d.Output) : null,
        d.Kind == DraftKind.Rewrite ? DraftReader.Rewrite(d.Output) : null,
        d.Kind == DraftKind.Translation ? DraftReader.StoredTranslation(d.Output) : null);
}

/// <summary>Turns the model's JSON into typed drafts, tidying what it returned.</summary>
public static class DraftReader
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static JsonSerializerOptions Json => JsonOptions;

    /// <summary>The model's answer: title, summary and body in the target language.</summary>
    public static TranslationDraft Translation(string json, string language)
    {
        var raw = JsonSerializer.Deserialize<TranslationJson>(json, Json) ?? throw new JsonException("Empty translation.");
        if (string.IsNullOrWhiteSpace(raw.Title) || string.IsNullOrWhiteSpace(raw.Body))
        {
            throw new JsonException("The translation is missing its title or text.");
        }

        return new TranslationDraft(language, Languages.NameOf(language), Trim(raw.Title), Trim(raw.Summary), Trim(raw.Body));
    }

    /// <summary>A translation draft as saved (with its language).</summary>
    public static TranslationDraft StoredTranslation(string json) =>
        JsonSerializer.Deserialize<TranslationDraft>(json, Json) ?? throw new JsonException("Empty translation.");

    public static bool IsValid(DraftKind kind, string json)
    {
        try
        {
            return kind switch
            {
                DraftKind.SermonLesson => Lesson(json) is { Title.Length: > 0, Questions.Count: > 0 },
                DraftKind.SermonNotes => Notes(json) is { Summary.Length: > 0 },
                DraftKind.Rewrite => Rewrite(json) is { Text.Length: > 0 },
                DraftKind.Translation => StoredTranslation(json) is { Title.Length: > 0, Body.Length: > 0 },
                _ => false,
            };
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static LessonDraft Lesson(string json)
    {
        var raw = JsonSerializer.Deserialize<LessonJson>(json, Json) ?? throw new JsonException("Empty lesson.");
        var verses = Clean(raw.KeyVerses, 10);
        var questions = Clean(raw.Questions, 10);
        var body = new StringBuilder();
        body.AppendLine(Trim(raw.Summary)).AppendLine();
        if (verses.Count > 0)
        {
            body.Append("Key verses: ").AppendLine(string.Join("; ", verses)).AppendLine();
        }

        body.AppendLine("Icebreaker").AppendLine(Trim(raw.Icebreaker)).AppendLine();
        body.AppendLine("Discussion");
        for (var i = 0; i < questions.Count; i++)
        {
            body.Append(CultureInfo.InvariantCulture, $"{i + 1}. ").AppendLine(questions[i]);
        }

        body.AppendLine().AppendLine("This week").AppendLine(Trim(raw.Application)).AppendLine();
        body.AppendLine("Prayer").Append(Trim(raw.PrayerFocus));
        return new LessonDraft(Trim(raw.Title), Trim(raw.Summary), verses, Trim(raw.Icebreaker), questions, Trim(raw.Application), Trim(raw.PrayerFocus), body.ToString().Trim());
    }

    public static NotesDraft Notes(string json)
    {
        var raw = JsonSerializer.Deserialize<NotesJson>(json, Json) ?? throw new JsonException("Empty notes.");
        var summary = Trim(raw.Summary);
        return new NotesDraft(summary.Length > 1000 ? summary[..1000] : summary, Trim(raw.Notes), Clean(raw.Topics, 6));
    }

    public static RewriteDraft Rewrite(string json)
    {
        var raw = JsonSerializer.Deserialize<RewriteJson>(json, Json) ?? throw new JsonException("Empty rewrite.");
        return new RewriteDraft(Trim(raw.Text));
    }

    private static string Trim(string? value) => value?.Trim() ?? string.Empty;

    private static List<string> Clean(IEnumerable<string>? values, int max) =>
        (values ?? []).Select(Trim).Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(max).ToList();

    private sealed record LessonJson(string? Title, string? Summary, List<string>? KeyVerses, string? Icebreaker, List<string>? Questions, string? Application, string? PrayerFocus);

    private sealed record NotesJson(string? Summary, string? Notes, List<string>? Topics);

    private sealed record RewriteJson(string? Text);

    private sealed record TranslationJson(string? Title, string? Summary, string? Body);
}

/// <summary>Usage and cost for administrators.</summary>
public sealed class UsageService(AiGateway gateway, IAssistDb db, IOptions<AssistOptions> options, TimeProvider clock)
{
    public async Task<UsageDto> GetAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var thisMonth = AiGateway.MonthStart(now);
        var since = thisMonth.AddMonths(-5);
        var rows = await db.Usage.AsNoTracking()
            .Where(u => u.OccurredAt >= since)
            .Select(u => new { u.OccurredAt, u.CostZar })
            .ToListAsync(cancellationToken);
        var months = Enumerable.Range(0, 6)
            .Select(i => thisMonth.AddMonths(-i))
            .Select(start =>
            {
                var inMonth = rows.Where(r => r.OccurredAt >= start && r.OccurredAt < start.AddMonths(1)).ToList();
                return new UsageMonthDto(start.ToString("yyyy-MM", CultureInfo.InvariantCulture), inMonth.Count, Math.Round(inMonth.Sum(r => r.CostZar), 2));
            })
            .ToList();
        var recent = await db.Usage.AsNoTracking()
            .OrderByDescending(u => u.OccurredAt)
            .Take(50)
            .Select(u => new UsageCallDto(u.OccurredAt, u.Operation, u.Purpose, u.Model, u.InputTokens, u.OutputTokens, u.AudioSeconds, u.CostZar, u.Succeeded))
            .ToListAsync(cancellationToken);
        var o = options.Value;
        return new UsageDto(o.IsEnabled, o.Provider, gateway.ChatModel, o.MonthlyBudgetZar, months[0].CostZar, months, recent);
    }
}

/// <summary>Drafts are deleted after 90 days; the usage log (no content) after 13 months, so a year can be compared.</summary>
public sealed class AssistRetentionJob(IAssistDb db, TimeProvider clock)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var draftCutoff = now - AiDraft.Retention;
        await db.Drafts.Where(d => d.RequestedAt < draftCutoff).ExecuteDeleteAsync(cancellationToken);
        var usageCutoff = now.AddMonths(-13);
        await db.Usage.Where(u => u.OccurredAt < usageCutoff).ExecuteDeleteAsync(cancellationToken);
    }
}
