namespace Shapers.Media.Domain;

public sealed class Speaker : AggregateRoot<Guid>
{
    private Speaker()
    {
    }

    public string Name { get; private set; } = null!;

    /// <summary>e.g. "Senior Pastor". Shown under the name.</summary>
    public string? Title { get; private set; }

    public string? Bio { get; private set; }

    public Guid? PhotoAssetId { get; private set; }

    /// <summary>Set when the speaker is someone in the church database. Guest speakers don't need a record.</summary>
    public Guid? PersonId { get; private set; }

    public bool IsActive { get; private set; }

    public static Speaker Create(string name, string? title, string? bio, Guid? personId)
    {
        var speaker = new Speaker { Id = Guid.CreateVersion7(), IsActive = true };
        speaker.Update(name, title, bio, personId);
        return speaker;
    }

    public void Update(string name, string? title, string? bio, Guid? personId)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120)
        {
            throw new DomainRuleException("media.speaker_name", "A speaker needs a name (120 characters at most).");
        }

        Name = name.Trim();
        Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        Bio = string.IsNullOrWhiteSpace(bio) ? null : bio.Trim();
        PersonId = personId;
    }

    public void SetPhoto(Guid? assetId) => PhotoAssetId = assetId;

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;
}

public sealed class Series : AggregateRoot<Guid>
{
    private Series()
    {
    }

    public string Title { get; private set; } = null!;

    public string Slug { get; private set; } = null!;

    public string? Description { get; private set; }

    public Guid? ArtworkAssetId { get; private set; }

    public DateOnly? StartsOn { get; private set; }

    public DateOnly? EndsOn { get; private set; }

    public string Scope { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public static Series Create(string title, ScopePath scope, DateTimeOffset now)
    {
        var series = new Series { Id = Guid.CreateVersion7(), Scope = scope.Value, CreatedAt = now };
        series.Update(title, null, null, null);
        series.Slug = Sermon.SlugWords(series.Title, "series");
        return series;
    }

    public void Update(string title, string? description, DateOnly? startsOn, DateOnly? endsOn)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 150)
        {
            throw new DomainRuleException("media.series_title", "A series needs a title (150 characters at most).");
        }

        if (startsOn is { } s && endsOn is { } e && e < s)
        {
            throw new DomainRuleException("media.series_dates", "A series can't end before it starts.");
        }

        Title = title.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        StartsOn = startsOn;
        EndsOn = endsOn;
    }

    public void SetArtwork(Guid? assetId) => ArtworkAssetId = assetId;

    public void UseSlug(string slug) => Slug = slug;
}

public enum MediaKind
{
    Audio,
    NotesPdf,
    Image,
}

public enum MediaAssetStatus
{
    Pending,
    Ready,
}

/// <summary>
/// A file in object storage. Created as Pending when an upload starts; becomes Ready once the file is
/// confirmed in storage. Files never pass through the database.
/// </summary>
public sealed class MediaAsset : AggregateRoot<Guid>
{
    public static readonly IReadOnlyDictionary<MediaKind, (long MaxBytes, string[] ContentTypes)> Rules =
        new Dictionary<MediaKind, (long, string[])>
        {
            [MediaKind.Audio] = (300L * 1024 * 1024, ["audio/mpeg", "audio/mp4", "audio/x-m4a", "audio/aac"]),
            [MediaKind.NotesPdf] = (20L * 1024 * 1024, ["application/pdf"]),
            [MediaKind.Image] = (10L * 1024 * 1024, ["image/jpeg", "image/png", "image/webp"]),
        };

    private MediaAsset()
    {
    }

    public MediaKind Kind { get; private set; }

    public string StorageKey { get; private set; } = null!;

    public string ContentType { get; private set; } = null!;

    public string OriginalFileName { get; private set; } = null!;

    public long SizeBytes { get; private set; }

    public int? DurationSeconds { get; private set; }

    public MediaAssetStatus Status { get; private set; }

    public Guid? UploadedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static MediaAsset StartUpload(MediaKind kind, string fileName, string contentType, long sizeBytes, Guid? uploadedBy, DateTimeOffset now)
    {
        var (maxBytes, contentTypes) = Rules[kind];
        var type = contentType.Trim().ToLowerInvariant();
        if (!contentTypes.Contains(type))
        {
            throw new DomainRuleException("media.file_type", $"That file type isn't accepted here. Use {string.Join(", ", contentTypes)}.");
        }

        if (sizeBytes <= 0 || sizeBytes > maxBytes)
        {
            throw new DomainRuleException("media.file_size", $"Files must be smaller than {maxBytes / (1024 * 1024)} MB.");
        }

        var id = Guid.CreateVersion7();
        var extension = Path.GetExtension(fileName).ToLowerInvariant() is { Length: > 1 and <= 6 } ext && ext.All(c => char.IsAsciiLetterOrDigit(c) || c == '.')
            ? ext
            : string.Empty;
        return new MediaAsset
        {
            Id = id,
            Kind = kind,
            StorageKey = $"{kind.ToString().ToLowerInvariant()}/{now:yyyy}/{id:N}{extension}",
            ContentType = type,
            OriginalFileName = Path.GetFileName(fileName).Trim() is { Length: > 0 and <= 200 } name ? name : "upload",
            SizeBytes = sizeBytes,
            Status = MediaAssetStatus.Pending,
            UploadedByUserId = uploadedBy,
            CreatedAt = now,
        };
    }

    /// <summary>Confirms the upload. The stored size must match what was declared.</summary>
    public void MarkReady(long storedBytes, int? durationSeconds)
    {
        if (storedBytes != SizeBytes)
        {
            throw new DomainRuleException("media.upload_incomplete", "The upload didn't finish. Please try again.");
        }

        DurationSeconds = durationSeconds is > 0 and < 24 * 3600 ? durationSeconds : null;
        Status = MediaAssetStatus.Ready;
    }
}

/// <summary>
/// Where a member got to in a sermon, so they can carry on. Personal data: kept only as the latest
/// position and deleted after six months.
/// </summary>
public sealed class PlaybackPosition
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(183);

    private PlaybackPosition()
    {
    }

    public Guid PersonId { get; private set; }

    public Guid SermonId { get; private set; }

    public int PositionSeconds { get; private set; }

    public bool Completed { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static PlaybackPosition Start(Guid personId, Guid sermonId, DateTimeOffset now) =>
        new() { PersonId = personId, SermonId = sermonId, UpdatedAt = now };

    public void Update(int positionSeconds, int? durationSeconds, DateTimeOffset now)
    {
        PositionSeconds = Math.Max(0, positionSeconds);
        // Within the last 30 seconds counts as finished, so outros don't leave sermons "in progress" forever.
        Completed = durationSeconds is > 0 && PositionSeconds >= durationSeconds.Value - 30;
        UpdatedAt = now;
    }
}
