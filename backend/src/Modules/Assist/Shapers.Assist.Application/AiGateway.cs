using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shapers.Assist.Contracts;
using Shapers.Assist.Domain;
using Shapers.Platform.Authorization;

namespace Shapers.Assist.Application;

/// <summary>
/// The only way to reach an AI service. Refuses when AI is switched off or the month's budget is spent,
/// and logs every call (never its content) with an estimated cost.
/// </summary>
public sealed partial class AiGateway(
    IAiProvider provider,
    IAssistDb db,
    IOptions<AssistOptions> options,
    ICurrentUser currentUser,
    TimeProvider clock,
    ILogger<AiGateway> logger)
{
    /// <summary>The church's months follow Johannesburg time (UTC+2, no daylight saving).</summary>
    private static readonly TimeSpan ChurchOffset = TimeSpan.FromHours(2);

    public static readonly Error Disabled = new("assist.disabled", "AI help is switched off.");

    public bool IsEnabled => options.Value.IsEnabled;

    public string ChatModel => provider.ChatModel;

    public static DateTimeOffset MonthStart(DateTimeOffset now)
    {
        var local = now.ToOffset(ChurchOffset);
        return new DateTimeOffset(local.Year, local.Month, 1, 0, 0, 0, ChurchOffset);
    }

    public async Task<decimal> SpentThisMonthAsync(CancellationToken cancellationToken)
    {
        var start = MonthStart(clock.GetUtcNow());
        return await db.Usage.Where(u => u.OccurredAt >= start).SumAsync(u => u.CostZar, cancellationToken);
    }

    public async Task<Result<ChatResponse>> ChatAsync(string purpose, ChatRequest request, CancellationToken cancellationToken)
    {
        var refused = await RefuseAsync(cancellationToken);
        if (refused is not null)
        {
            return refused;
        }

        try
        {
            var response = await provider.ChatAsync(request, cancellationToken);
            var prices = options.Value.Prices;
            var cost = (response.InputTokens * prices.ChatInputPerMillionTokensZar + response.OutputTokens * prices.ChatOutputPerMillionTokensZar) / 1_000_000m;
            await RecordAsync(AiUsage.Record(AiOperation.Chat, purpose, response.Model, response.InputTokens, response.OutputTokens, 0, cost, currentUser.UserId, true, clock.GetUtcNow()), cancellationToken);
            return response;
        }
        catch (AiProviderException ex)
        {
            LogFailure(logger, purpose, ex.Code, ex);
            await RecordAsync(AiUsage.Record(AiOperation.Chat, purpose, provider.ChatModel, 0, 0, 0, 0, currentUser.UserId, false, clock.GetUtcNow()), cancellationToken);
            return new Error(ex.Code, ex.Message);
        }
    }

    public async Task<Result<TranscriptResponse>> TranscribeAsync(string purpose, Stream audio, string fileName, string contentType, string locale, CancellationToken cancellationToken)
    {
        var refused = await RefuseAsync(cancellationToken);
        if (refused is not null)
        {
            return refused;
        }

        try
        {
            var response = await provider.TranscribeAsync(audio, fileName, contentType, locale, cancellationToken);
            var cost = response.DurationSeconds / 3600m * options.Value.Prices.TranscriptionPerHourZar;
            await RecordAsync(AiUsage.Record(AiOperation.Transcription, purpose, response.Model, 0, 0, response.DurationSeconds, cost, currentUser.UserId, true, clock.GetUtcNow()), cancellationToken);
            return response;
        }
        catch (AiProviderException ex)
        {
            LogFailure(logger, purpose, ex.Code, ex);
            await RecordAsync(AiUsage.Record(AiOperation.Transcription, purpose, "speech", 0, 0, 0, 0, currentUser.UserId, false, clock.GetUtcNow()), cancellationToken);
            return new Error(ex.Code, ex.Message);
        }
    }

    private async Task<Error?> RefuseAsync(CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return Disabled;
        }

        return await SpentThisMonthAsync(cancellationToken) >= options.Value.MonthlyBudgetZar
            ? new Error("assist.budget_reached", "This month's AI budget has been used. It resets on the 1st, or an administrator can raise it.", ErrorKind.Conflict)
            : null;
    }

    private async Task RecordAsync(AiUsage usage, CancellationToken cancellationToken)
    {
        db.Usage.Add(usage);
        await db.SaveChangesAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "AI call for {Purpose} failed ({Code})")]
    private static partial void LogFailure(ILogger logger, string purpose, string code, Exception exception);
}

/// <summary>Speech to text for other modules, through the gateway so it counts towards the budget.</summary>
public sealed class AssistTranscriber(AiGateway gateway) : IAssistTranscriber
{
    public bool IsEnabled => gateway.IsEnabled;

    public async Task<TranscriptionOutcome> TranscribeAsync(Stream audio, string fileName, string contentType, string locale, string purpose, CancellationToken cancellationToken)
    {
        var result = await gateway.TranscribeAsync(purpose, audio, fileName, contentType, locale, cancellationToken);
        return result.IsSuccess
            ? TranscriptionOutcome.Ok(result.Value.Text)
            : TranscriptionOutcome.Failed(result.Error!.Code, result.Error.Message);
    }
}
