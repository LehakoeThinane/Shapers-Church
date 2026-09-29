using Shapers.Media.Domain;

namespace Shapers.Media.Application;

public sealed record SpeakerDto(Guid Id, string Name, string? Title, string? Bio, string? PhotoUrl, Guid? PersonId, bool IsActive);

public sealed record SeriesDto(Guid Id, string Title, string Slug, string? Description, string? ArtworkUrl, DateOnly? StartsOn, DateOnly? EndsOn, string Scope, int SermonCount);

public sealed record VideoDto(VideoProvider Provider, string ExternalId, string WatchUrl, string ThumbnailUrl);

public sealed record AudioDto(Guid AssetId, string Url, string ContentType, long SizeBytes, int? DurationSeconds);

public sealed record ScriptureDto(string Display, int BookNumber, string Book, int ChapterFrom, int? VerseFrom, int ChapterTo, int? VerseTo);

/// <summary>A sermon as members and the public see it.</summary>
public sealed record SermonSummaryDto(
    Guid Id,
    string Title,
    string Slug,
    DateOnly PreachedOn,
    IReadOnlyList<string> Speakers,
    string? SeriesTitle,
    string? SeriesSlug,
    IReadOnlyList<string> Scripture,
    string? ThumbnailUrl,
    int? AudioDurationSeconds,
    bool HasAudio,
    bool HasVideo);

public sealed record SermonDetailDto(
    Guid Id,
    string Title,
    string Slug,
    DateOnly PreachedOn,
    string? Summary,
    string? Notes,
    IReadOnlyList<SpeakerDto> Speakers,
    SeriesDto? Series,
    IReadOnlyList<ScriptureDto> Scripture,
    IReadOnlyList<string> Topics,
    VideoDto? Video,
    AudioDto? Audio,
    string? NotesPdfUrl,
    DateTimeOffset? PublishedAt);

/// <summary>The editor's view, including status and anything blocking publication.</summary>
public sealed record SermonAdminDto(
    SermonDetailDto Sermon,
    SermonStatus Status,
    DateTimeOffset? PublishAt,
    string Scope,
    string? ImportSource,
    IReadOnlyList<string> PublishProblems,
    DateTimeOffset UpdatedAt);

public sealed record SermonAdminListItemDto(
    Guid Id,
    string Title,
    DateOnly PreachedOn,
    SermonStatus Status,
    DateTimeOffset? PublishAt,
    IReadOnlyList<string> Speakers,
    string? SeriesTitle,
    bool HasAudio,
    bool HasVideo);

public sealed record SaveSermonRequest(
    string Title,
    DateOnly PreachedOn,
    Guid? SeriesId,
    string? Summary,
    string? Notes,
    IReadOnlyList<string> Topics,
    IReadOnlyList<Guid> SpeakerIds,
    string? Scripture,
    string? VideoUrl,
    string? Scope);

public sealed record SetSermonAssetRequest(Guid? AssetId);

public sealed record ScheduleRequest(DateTimeOffset PublishAt);

public sealed record SaveSeriesRequest(string Title, string? Description, DateOnly? StartsOn, DateOnly? EndsOn, Guid? ArtworkAssetId);

public sealed record SaveSpeakerRequest(string Name, string? Title, string? Bio, Guid? PersonId, Guid? PhotoAssetId);

public sealed record StartUploadRequest(MediaKind Kind, string FileName, string ContentType, long SizeBytes);

public sealed record StartUploadResponse(Guid AssetId, UploadTarget Upload);

public sealed record CompleteUploadRequest(int? DurationSeconds);

public sealed record AssetDto(Guid Id, MediaKind Kind, string Url, string ContentType, long SizeBytes, int? DurationSeconds, MediaAssetStatus Status);

public sealed record ScriptureCheckDto(IReadOnlyList<string> Recognised, IReadOnlyList<string> Unrecognised);

public sealed record SermonSearchQuery(string? Q, string? Series, Guid? SpeakerId, int? Book, string? Topic, int? Year, int Page = 1, int PageSize = 20);

public sealed record AdminSermonQuery(string? Q, SermonStatus? Status, Guid? SeriesId, int Page = 1, int PageSize = 25);

public sealed record PlaybackUpdateRequest(int PositionSeconds);

public sealed record ContinueListeningDto(SermonSummaryDto Sermon, int PositionSeconds, DateTimeOffset UpdatedAt);

public sealed record ImportResultDto(int Found, int Created, int AlreadyImported, IReadOnlyList<string> CreatedTitles);
