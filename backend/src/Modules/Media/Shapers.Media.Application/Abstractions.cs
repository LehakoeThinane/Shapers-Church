using Microsoft.EntityFrameworkCore;
using Shapers.Media.Contracts;
using Shapers.Media.Domain;
using Shapers.Platform.Authorization;

namespace Shapers.Media.Application;

public interface IMediaDb
{
    DbSet<Sermon> Sermons { get; }

    DbSet<Series> Series { get; }

    DbSet<Speaker> Speakers { get; }

    DbSet<MediaAsset> Assets { get; }

    DbSet<PlaybackPosition> PlaybackPositions { get; }

    DbSet<Livestream> Livestreams { get; }

    DbSet<ChatMessage> ChatMessages { get; }

    DbSet<ChatSanction> ChatSanctions { get; }

    DbSet<ChatBlockedTerm> ChatBlockedTerms { get; }

    DbSet<YouTubeConnection> YouTubeConnections { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Where the browser or app should send the file, and the headers it must include.</summary>
public sealed record UploadTarget(string Url, string Method, IReadOnlyDictionary<string, string> Headers, DateTimeOffset ExpiresAt);

/// <summary>
/// Object storage. Azure Blob Storage (South Africa North) in hosted environments, the local disk in
/// development. Uploads go straight to storage, never through the API.
/// </summary>
public interface IFileStorage
{
    UploadTarget CreateUpload(string key, string contentType, long sizeBytes, TimeSpan lifetime);

    /// <summary>The stored size, or null when nothing has been uploaded yet.</summary>
    Task<long?> GetSizeAsync(string key, CancellationToken cancellationToken);

    string PublicUrl(string key);

    /// <summary>Reads a stored file, e.g. sermon audio for transcription.</summary>
    Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken);

    Task DeleteAsync(string key, CancellationToken cancellationToken);
}

public sealed record YouTubeVideo(string VideoId, string Title, DateTimeOffset PublishedAt, string? Description);

public interface IYouTubeClient
{
    bool IsConfigured { get; }

    /// <summary>Every public video uploaded to the channel, newest first.</summary>
    Task<IReadOnlyList<YouTubeVideo>> ListChannelVideosAsync(string channelId, CancellationToken cancellationToken);
}

public sealed record CaptionTrack(string Id, string Language, string TrackKind, bool IsDraft);

public sealed record YouTubeChannel(string Id, string Title);

/// <summary>
/// YouTube with the channel owner's permission (OAuth): reads the channel's caption tracks. YouTube only lets a
/// channel's owner download its captions, so the owner connects the channel once from the admin portal.
/// </summary>
public interface IYouTubeCaptions
{
    /// <summary>True when the church's Google OAuth client is set up (Media:YouTube:OAuthClientId and secret).</summary>
    bool IsConfigured { get; }

    string AuthorizeUrl(string state, string redirectUri);

    /// <summary>Exchanges the code from Google's redirect for a refresh token.</summary>
    Task<string> ExchangeCodeAsync(string code, string redirectUri, CancellationToken cancellationToken);

    Task<string> AccessTokenAsync(string refreshToken, CancellationToken cancellationToken);

    Task<YouTubeChannel> MyChannelAsync(string accessToken, CancellationToken cancellationToken);

    Task<IReadOnlyList<CaptionTrack>> ListCaptionsAsync(string accessToken, string videoId, CancellationToken cancellationToken);

    /// <summary>The track as SubRip (.srt) text.</summary>
    Task<string> DownloadSrtAsync(string accessToken, string trackId, CancellationToken cancellationToken);

    Task RevokeAsync(string refreshToken, CancellationToken cancellationToken);
}

/// <summary>A failure talking to YouTube, worded for staff.</summary>
public sealed class YouTubeException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Encrypts credentials kept in the database and signs short-lived values such as OAuth state.</summary>
public interface ITokenProtector
{
    string Protect(string value);

    /// <summary>Null when the value can't be read, e.g. the keys changed.</summary>
    string? Unprotect(string value);

    string ProtectFor(string value, TimeSpan lifetime);

    /// <summary>Null when it's been tampered with or has expired.</summary>
    string? UnprotectTimed(string value);
}

public sealed class MediaOptions
{
    public const string SectionName = "Media";

    public YouTubeOptions YouTube { get; set; } = new();

    public PodcastOptions Podcast { get; set; } = new();

    /// <summary>Public site used for links in the podcast feed, e.g. https://shaperschurch.com.</summary>
    public string SiteUrl { get; set; } = "https://shaperschurch.com";

    public sealed class YouTubeOptions
    {
        /// <summary>A read-only YouTube Data API key. Kept in secrets, never in the repository.</summary>
        public string? ApiKey { get; set; }

        public string ChannelId { get; set; } = "UCZf66xLSk4RyXbMzI_lVf-g";

        /// <summary>The church's Google OAuth client (web application), for reading captions with the owner's permission.</summary>
        public string? OAuthClientId { get; set; }

        public string? OAuthClientSecret { get; set; }

        /// <summary>
        /// Where Google sends the owner back, registered in the Google Cloud console, e.g.
        /// https://api.shaperschurch.com/api/media/youtube/callback. Defaults to this API's address.
        /// </summary>
        public string? OAuthRedirectUri { get; set; }
    }

    public sealed class PodcastOptions
    {
        public string Title { get; set; } = "Shapers Church";

        public string Author { get; set; } = "Shapers Church";

        public string Description { get; set; } = "Sermons from Shapers Church, Johannesburg: building productive people for the kingdom of God.";

        public string Email { get; set; } = "info@shaperschurch.com";

        public string? ImageUrl { get; set; }

        public string Language { get; set; } = "en-za";

        public string Category { get; set; } = "Religion & Spirituality";

        public string SubCategory { get; set; } = "Christianity";
    }
}

public sealed class MediaPermissionProvider : IPermissionProvider
{
    public IEnumerable<PermissionDefinition> GetPermissions() =>
    [
        new(MediaPermissions.SermonsEdit, "media", "Create and edit sermons, series and uploads"),
        new(MediaPermissions.SermonsPublish, "media", "Publish, schedule and archive sermons"),
        new(MediaPermissions.SpeakersManage, "media", "Add and edit speakers"),
        new(MediaPermissions.LivestreamManage, "media", "Schedule livestreams, go live and show scripture"),
        new(MediaPermissions.ChatModerate, "media", "Moderate livestream chat: hide messages, time out and ban, slow mode, word list"),
    ];
}

/// <summary>Pushes chat changes to everyone watching a livestream. The API implements it with SignalR.</summary>
public interface IChatBroadcaster
{
    Task MessageAsync(Guid livestreamId, ChatMessageDto message, CancellationToken cancellationToken);

    Task RemovedAsync(Guid livestreamId, Guid messageId, CancellationToken cancellationToken);

    Task RulesAsync(Guid livestreamId, ChatRulesDto rules, CancellationToken cancellationToken);
}
