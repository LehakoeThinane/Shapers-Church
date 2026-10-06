using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shapers.Church.Contracts;
using Shapers.Media.Domain;
using Shapers.Platform.Auditing;
using Shapers.Platform.Authorization;

namespace Shapers.Media.Application;

public sealed record YouTubeConnectionDto(
    bool Configured,
    bool Connected,
    string? ChannelId,
    string? ChannelTitle,
    DateTimeOffset? ConnectedAt,
    bool IsChurchChannel,
    int SermonsWaiting);

public sealed record YouTubeConnectStartDto(string AuthorizeUrl);

/// <summary>
/// Sermon transcripts from YouTube captions. The channel owner connects the channel once; after that, sermons with a
/// YouTube video and no transcript get the video's captions (the church's own, or YouTube's automatic ones).
/// </summary>
public sealed partial class YouTubeCaptionService(
    IMediaDb db,
    IYouTubeCaptions youtube,
    ITokenProtector protector,
    IOptions<MediaOptions> options,
    ICurrentUser currentUser,
    IChurchDirectory church,
    IAuditLog audit,
    TimeProvider clock,
    ILogger<YouTubeCaptionService> logger)
{
    public const int BatchSize = 10;

    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(15);

    public async Task<YouTubeConnectionDto> StatusAsync(CancellationToken cancellationToken)
    {
        var connection = await db.YouTubeConnections.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var waiting = connection is null ? 0 : await PendingQuery().CountAsync(cancellationToken);
        return new YouTubeConnectionDto(
            youtube.IsConfigured,
            connection is not null,
            connection?.ChannelId,
            connection?.ChannelTitle,
            connection?.ConnectedAt,
            connection is not null && connection.ChannelId == options.Value.YouTube.ChannelId,
            waiting);
    }

    /// <summary>The Google sign-in page the owner opens. The state ties Google's answer to this request for 15 minutes.</summary>
    public Result<YouTubeConnectStartDto> StartConnect(string redirectUri)
    {
        if (!youtube.IsConfigured)
        {
            return new Error("media.youtube_not_configured", "Connecting YouTube isn't set up yet (Media:YouTube:OAuthClientId and secret).");
        }

        var state = protector.ProtectFor($"{currentUser.UserId}|{Guid.NewGuid():N}", StateLifetime);
        return new YouTubeConnectStartDto(youtube.AuthorizeUrl(state, redirectUri));
    }

    /// <summary>Google's redirect back. Runs without the staff cookie (it is cross-site), so the signed state says who started it.</summary>
    public async Task<Result<string>> CompleteAsync(string? code, string? state, string? error, string redirectUri, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(error))
        {
            return new Error("media.youtube_declined", "YouTube access wasn't given, so nothing was connected.");
        }

        var payload = state is null ? null : protector.UnprotectTimed(state);
        if (payload is null || code is null || !Guid.TryParse(payload.Split('|')[0], out var userId))
        {
            return new Error("media.youtube_state", "This link has expired. Start again from the admin portal.");
        }

        try
        {
            var refreshToken = await youtube.ExchangeCodeAsync(code, redirectUri, cancellationToken);
            var accessToken = await youtube.AccessTokenAsync(refreshToken, cancellationToken);
            var channel = await youtube.MyChannelAsync(accessToken, cancellationToken);

            await db.YouTubeConnections.ExecuteDeleteAsync(cancellationToken);
            var connection = YouTubeConnection.Connect(channel.Id, channel.Title, protector.Protect(refreshToken), userId, clock.GetUtcNow());
            db.YouTubeConnections.Add(connection);
            await db.SaveChangesAsync(cancellationToken);
            await AuditAsync("media.youtube.connected", new { channel.Id, channel.Title, ConnectedBy = userId }, cancellationToken);
            return channel.Title;
        }
        catch (YouTubeException ex)
        {
            LogYouTubeFailed(logger, "connect", ex);
            return new Error("media.youtube_failed", ex.Message);
        }
    }

    public async Task<Result> DisconnectAsync(CancellationToken cancellationToken)
    {
        var connection = await db.YouTubeConnections.FirstOrDefaultAsync(cancellationToken);
        if (connection is null)
        {
            return Result.Success();
        }

        if (protector.Unprotect(connection.ProtectedRefreshToken) is { } token)
        {
            try
            {
                await youtube.RevokeAsync(token, cancellationToken);
            }
            catch (YouTubeException ex)
            {
                // Removing it here still stops all use; the owner can also remove access in their Google account.
                LogYouTubeFailed(logger, "revoke", ex);
            }
        }

        db.YouTubeConnections.Remove(connection);
        await db.SaveChangesAsync(cancellationToken);
        await AuditAsync("media.youtube.disconnected", new { connection.ChannelId }, cancellationToken);
        return Result.Success();
    }

    /// <summary>Fetches captions for one sermon now (the "Get captions" button). Replaces a pasted transcript only if asked.</summary>
    public async Task<Result> ImportForSermonAsync(Guid sermonId, CancellationToken cancellationToken)
    {
        var sermon = await db.Sermons.SingleOrDefaultAsync(s => s.Id == sermonId, cancellationToken);
        if (sermon is null)
        {
            return Error.NotFound("media.sermon_not_found", "Sermon not found.");
        }

        if (sermon.Video is null)
        {
            return new Error("media.no_video", "Add the sermon's YouTube link first.");
        }

        var access = await AccessTokenAsync(cancellationToken);
        if (access.IsFailure)
        {
            return access.Error!;
        }

        var imported = await ImportAsync(sermon, access.Value, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return imported ? Result.Success() : new Error("media.no_captions", sermon.TranscriptError ?? "This video has no captions yet.");
    }

    /// <summary>Hourly: captions for sermons with a video and no transcript, a few at a time (YouTube's daily quota is small).</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (!youtube.IsConfigured || !await db.YouTubeConnections.AnyAsync(cancellationToken))
        {
            return;
        }

        var access = await AccessTokenAsync(cancellationToken);
        if (access.IsFailure)
        {
            return;
        }

        var sermons = await PendingQuery().OrderByDescending(s => s.PreachedOn).Take(BatchSize).ToListAsync(cancellationToken);
        foreach (var sermon in sermons)
        {
            await ImportAsync(sermon, access.Value, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private IQueryable<Sermon> PendingQuery() =>
        db.Sermons.Where(s => s.Video != null && s.TranscriptStatus == TranscriptStatus.None && s.Status != SermonStatus.Archived);

    private async Task<Result<string>> AccessTokenAsync(CancellationToken cancellationToken)
    {
        var connection = await db.YouTubeConnections.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (connection is null)
        {
            return new Error("media.youtube_not_connected", "Connect the church's YouTube channel first.");
        }

        var refreshToken = protector.Unprotect(connection.ProtectedRefreshToken);
        if (refreshToken is null)
        {
            return new Error("media.youtube_reconnect", "The YouTube connection can't be read any more. Connect the channel again.");
        }

        try
        {
            return await youtube.AccessTokenAsync(refreshToken, cancellationToken);
        }
        catch (YouTubeException ex)
        {
            LogYouTubeFailed(logger, "token", ex);
            return new Error("media.youtube_reconnect", "YouTube no longer accepts the connection. Connect the channel again.");
        }
    }

    private async Task<bool> ImportAsync(Sermon sermon, string accessToken, CancellationToken cancellationToken)
    {
        try
        {
            var tracks = await youtube.ListCaptionsAsync(accessToken, sermon.Video!.ExternalId, cancellationToken);
            var track = ChooseTrack(tracks);
            if (track is null)
            {
                sermon.FailTranscription("This video has no English captions on YouTube yet. Try again later, upload the audio, or paste the transcript.", clock.GetUtcNow());
                return false;
            }

            var text = SrtText.ToPlainText(await youtube.DownloadSrtAsync(accessToken, track.Id, cancellationToken));
            if (text.Length == 0)
            {
                sermon.FailTranscription("The captions on YouTube are empty.", clock.GetUtcNow());
                return false;
            }

            sermon.SetTranscript(text.Length > Sermon.MaxTranscriptLength ? text[..Sermon.MaxTranscriptLength] : text, TranscriptSource.Captions, clock.GetUtcNow());
            return true;
        }
        catch (YouTubeException ex)
        {
            LogYouTubeFailed(logger, "captions", ex);
            sermon.FailTranscription(ex.Message, clock.GetUtcNow());
            return false;
        }
    }

    /// <summary>The church's own English captions first, then YouTube's automatic ones; drafts are skipped.</summary>
    public static CaptionTrack? ChooseTrack(IEnumerable<CaptionTrack> tracks) =>
        tracks
            .Where(t => !t.IsDraft && t.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.TrackKind.Equals("asr", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .FirstOrDefault();

    private async Task AuditAsync(string action, object data, CancellationToken cancellationToken)
    {
        var root = ScopePath.Parse((await church.GetRootScopeAsync(cancellationToken)).Path);
        await audit.RecordAsync(new AuditRecord(action, "youtube_connection", "church", root, data), cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "YouTube {Step} failed")]
    private static partial void LogYouTubeFailed(ILogger logger, string step, Exception exception);
}

/// <summary>Turns SubRip captions into readable text: no numbers or timings, no repeated lines, paragraphs at pauses.</summary>
public static partial class SrtText
{
    /// <summary>A pause this long between captions starts a new paragraph.</summary>
    private static readonly TimeSpan ParagraphPause = TimeSpan.FromSeconds(2.5);

    public static string ToPlainText(string srt)
    {
        var text = new StringBuilder();
        string? previousLine = null;
        TimeSpan? previousEnd = null;
        foreach (var block in Blocks().Split(srt.Replace("\r\n", "\n", StringComparison.Ordinal).Trim()))
        {
            var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var timing = lines.Select(l => Timing().Match(l)).FirstOrDefault(m => m.Success);
            if (timing is not null && previousEnd is { } end && Parse(timing.Groups["start"].Value) - end >= ParagraphPause && text.Length > 0)
            {
                text.Append("\n\n");
            }

            if (timing is not null)
            {
                previousEnd = Parse(timing.Groups["end"].Value);
            }

            foreach (var raw in lines)
            {
                if (Timing().IsMatch(raw) || Number().IsMatch(raw))
                {
                    continue;
                }

                var line = WebUtility.HtmlDecode(Tags().Replace(raw, string.Empty)).Trim();
                if (line.Length == 0 || line.StartsWith('[') && line.EndsWith(']') || line == previousLine)
                {
                    continue;
                }

                if (text.Length > 0 && text[^1] != '\n')
                {
                    text.Append(' ');
                }

                text.Append(line);
                previousLine = line;
            }
        }

        return text.ToString().Trim();
    }

    private static TimeSpan Parse(string value) =>
        TimeSpan.ParseExact(value.Replace('.', ','), @"hh\:mm\:ss\,fff", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"\n\s*\n")]
    private static partial Regex Blocks();

    [GeneratedRegex(@"^(?<start>\d{2}:\d{2}:\d{2}[,.]\d{3})\s*-->\s*(?<end>\d{2}:\d{2}:\d{2}[,.]\d{3})")]
    private static partial Regex Timing();

    [GeneratedRegex(@"^\d+$")]
    private static partial Regex Number();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();
}
