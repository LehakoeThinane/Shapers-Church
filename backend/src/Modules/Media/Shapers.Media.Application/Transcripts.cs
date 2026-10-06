using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shapers.Assist.Contracts;
using Shapers.Media.Contracts;
using Shapers.Media.Domain;

namespace Shapers.Media.Application;

/// <summary>
/// Turns queued sermon audio into text, a couple of sermons at a time. Speech is transcribed in South Africa North;
/// the Assist module counts it towards the monthly AI budget.
/// </summary>
public sealed partial class TranscriptionJob(IMediaDb db, IFileStorage storage, IAssistTranscriber transcriber, TimeProvider clock, ILogger<TranscriptionJob> logger)
{
    /// <summary>A transcription still "working" after this long was interrupted (e.g. a restart) and is tried again.</summary>
    private static readonly TimeSpan Stuck = TimeSpan.FromMinutes(30);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (!transcriber.IsEnabled)
        {
            return;
        }

        var now = clock.GetUtcNow();
        var stuckBefore = now - Stuck;
        await db.Sermons
            .Where(s => s.TranscriptStatus == TranscriptStatus.Working && s.TranscriptUpdatedAt < stuckBefore)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.TranscriptStatus, TranscriptStatus.Queued), cancellationToken);

        var queued = await db.Sermons
            .Where(s => s.TranscriptStatus == TranscriptStatus.Queued)
            .OrderBy(s => s.TranscriptUpdatedAt)
            .Take(2)
            .ToListAsync(cancellationToken);
        foreach (var sermon in queued)
        {
            await TranscribeAsync(sermon, cancellationToken);
        }
    }

    private async Task TranscribeAsync(Sermon sermon, CancellationToken cancellationToken)
    {
        sermon.StartTranscription(clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);

        var asset = sermon.AudioAssetId is { } assetId ? await db.Assets.AsNoTracking().SingleOrDefaultAsync(a => a.Id == assetId, cancellationToken) : null;
        if (asset is null)
        {
            sermon.FailTranscription("The audio file is missing.", clock.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        TranscriptionOutcome outcome;
        try
        {
            await using var audio = await storage.OpenReadAsync(asset.StorageKey, cancellationToken);
            outcome = await transcriber.TranscribeAsync(audio, asset.OriginalFileName, asset.ContentType, "en-ZA", "sermon-transcript", cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogReadFailed(logger, sermon.Id, ex);
            outcome = TranscriptionOutcome.Failed("media.audio_unreadable", "The audio file couldn't be read.");
        }

        if (outcome.Succeeded)
        {
            sermon.CompleteTranscription(outcome.Text!, clock.GetUtcNow());
        }
        else
        {
            sermon.FailTranscription(outcome.ErrorMessage ?? "Transcription failed.", clock.GetUtcNow());
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't read the audio of sermon {SermonId} for transcription")]
    private static partial void LogReadFailed(ILogger logger, Guid sermonId, Exception exception);
}

/// <summary>Gives AI drafting a sermon's own words: title, speakers, passages and transcript. Nothing about listeners.</summary>
public sealed class SermonSource(IMediaDb db) : ISermonSource
{
    public async Task<SermonForDrafting?> GetForDraftingAsync(Guid sermonId, CancellationToken cancellationToken)
    {
        var sermon = await db.Sermons.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sermonId, cancellationToken);
        if (sermon is null)
        {
            return null;
        }

        var ids = sermon.Speakers.OrderBy(s => s.Order).Select(s => s.SpeakerId).ToList();
        var names = await db.Speakers.AsNoTracking().Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);
        return new SermonForDrafting(
            sermon.Id,
            sermon.Title,
            sermon.PreachedOn,
            sermon.Scope,
            ids.Select(id => names.GetValueOrDefault(id)).OfType<string>().ToList(),
            sermon.Scripture.Select(s => s.ToString()).ToList(),
            sermon.Summary,
            sermon.Transcript);
    }
}
