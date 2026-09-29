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

    Task DeleteAsync(string key, CancellationToken cancellationToken);
}

public sealed record YouTubeVideo(string VideoId, string Title, DateTimeOffset PublishedAt, string? Description);

public interface IYouTubeClient
{
    bool IsConfigured { get; }

    /// <summary>Every public video uploaded to the channel, newest first.</summary>
    Task<IReadOnlyList<YouTubeVideo>> ListChannelVideosAsync(string channelId, CancellationToken cancellationToken);
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
    ];
}
