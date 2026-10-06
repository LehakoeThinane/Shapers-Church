using System.Text.RegularExpressions;

namespace Shapers.Media.Domain;

public enum SermonStatus
{
    Draft,
    Scheduled,
    Published,
    Archived,
}

public enum VideoProvider
{
    YouTube,
}

/// <summary>Video stays with the provider; we only keep the reference. We never host or transcode video.</summary>
public sealed partial record VideoLink(VideoProvider Provider, string ExternalId)
{
    /// <summary>Accepts a YouTube video ID or any common YouTube URL (watch, youtu.be, embed, live, shorts).</summary>
    public static bool TryParseYouTube(string? input, out VideoLink video)
    {
        video = null!;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var value = input.Trim();
        var match = YouTubeUrl().Match(value);
        var id = match.Success ? match.Groups["id"].Value : value;
        if (!YouTubeId().IsMatch(id))
        {
            return false;
        }

        video = new VideoLink(VideoProvider.YouTube, id);
        return true;
    }

    public string WatchUrl => $"https://www.youtube.com/watch?v={ExternalId}";

    public string ThumbnailUrl => $"https://i.ytimg.com/vi/{ExternalId}/hqdefault.jpg";

    [GeneratedRegex(@"(?:youtube\.com/(?:watch\?(?:.*&)?v=|embed/|live/|shorts/|v/)|youtu\.be/)(?<id>[A-Za-z0-9_-]{11})")]
    private static partial Regex YouTubeUrl();

    [GeneratedRegex(@"^[A-Za-z0-9_-]{11}$")]
    private static partial Regex YouTubeId();
}

public enum TranscriptStatus
{
    None,
    Queued,
    Working,
    Ready,
    Failed,
}

public enum TranscriptSource
{
    Pasted,
    Audio,
    Captions,
}

public sealed record SermonPublished(Guid SermonId, string Title, string Slug, string Scope) : IDomainEvent;

/// <summary>
/// A preached message. Audio is the primary format (small, downloadable); video is an optional YouTube link.
/// Drafts are visible only to editors; publishing makes it public and tells other modules.
/// </summary>
public sealed partial class Sermon : AggregateRoot<Guid>
{
    private readonly List<SermonSpeaker> _speakers = [];
    private readonly List<ScriptureReference> _scripture = [];

    private Sermon()
    {
    }

    public string Title { get; private set; } = null!;

    /// <summary>Set once at creation so shared links never break when a title is edited.</summary>
    public string Slug { get; private set; } = null!;

    public Guid? SeriesId { get; private set; }

    public DateOnly PreachedOn { get; private set; }

    /// <summary>Which part of the church may edit it. Published sermons are public whatever their scope.</summary>
    public string Scope { get; private set; } = null!;

    public string? Summary { get; private set; }

    /// <summary>Sermon notes in Markdown.</summary>
    public string? Notes { get; private set; }

    public SermonStatus Status { get; private set; }

    public DateTimeOffset? PublishAt { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public string Language { get; private set; } = "en";

    public List<string> Topics { get; private set; } = [];

    public VideoLink? Video { get; private set; }

    public Guid? AudioAssetId { get; private set; }

    public int? AudioDurationSeconds { get; private set; }

    public Guid? NotesPdfAssetId { get; private set; }

    /// <summary>Where an imported sermon came from, e.g. "youtube:y0jPz7KFw_o". Makes imports repeatable.</summary>
    public string? ImportSource { get; private set; }

    /// <summary>Denormalised text (title, speakers, series, scripture, topics) kept for full-text search.</summary>
    public string SearchText { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>What was said, as text. Staff-only by default; AI drafting works from it.</summary>
    public string? Transcript { get; private set; }

    public TranscriptSource? TranscriptSource { get; private set; }

    public TranscriptStatus TranscriptStatus { get; private set; }

    public string? TranscriptError { get; private set; }

    public DateTimeOffset? TranscriptUpdatedAt { get; private set; }

    public const int MaxTranscriptLength = 300_000;

    public IReadOnlyList<SermonSpeaker> Speakers => _speakers;

    public IReadOnlyList<ScriptureReference> Scripture => _scripture;

    public bool IsPublic => Status == SermonStatus.Published;

    public static Sermon Create(string title, DateOnly preachedOn, ScopePath scope, DateTimeOffset now, string? importSource = null)
    {
        var sermon = new Sermon
        {
            Id = Guid.CreateVersion7(),
            Scope = scope.Value,
            PreachedOn = preachedOn,
            Status = SermonStatus.Draft,
            CreatedAt = now,
            UpdatedAt = now,
            ImportSource = importSource,
        };
        sermon.SetTitle(title);
        sermon.Slug = Slugify(sermon.Title, preachedOn);
        return sermon;
    }

    /// <summary>Replaces the generated slug, e.g. with "-2" appended when the same title was preached twice on one day.</summary>
    public void UseSlug(string slug)
    {
        if (!slug.StartsWith($"{PreachedOn:yyyy-MM-dd}-", StringComparison.Ordinal) || slug.Length > 90)
        {
            throw new DomainRuleException("media.slug_invalid", "Invalid sermon slug.");
        }

        Slug = slug;
    }

    public void UpdateDetails(string title, DateOnly preachedOn, Guid? seriesId, string? summary, string? notes, IEnumerable<string> topics, DateTimeOffset now)
    {
        EnsureEditable();
        SetTitle(title);
        PreachedOn = preachedOn;
        SeriesId = seriesId;
        Summary = Clean(summary, 1000, "summary");
        Notes = Clean(notes, 50_000, "notes");
        Topics = topics.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToList();
        UpdatedAt = now;
    }

    public void SetSpeakers(IReadOnlyList<Guid> speakerIds, DateTimeOffset now)
    {
        EnsureEditable();
        _speakers.Clear();
        _speakers.AddRange(speakerIds.Distinct().Select((id, i) => new SermonSpeaker(id, i)));
        UpdatedAt = now;
    }

    public void SetScripture(IEnumerable<ScriptureReference> references, DateTimeOffset now)
    {
        EnsureEditable();
        _scripture.Clear();
        _scripture.AddRange(references.Distinct().Take(20));
        UpdatedAt = now;
    }

    public void SetVideo(VideoLink? video, DateTimeOffset now)
    {
        EnsureEditable();
        Video = video;
        UpdatedAt = now;
    }

    public void AttachAudio(MediaAsset asset, DateTimeOffset now)
    {
        EnsureEditable();
        if (asset.Kind != MediaKind.Audio || asset.Status != MediaAssetStatus.Ready)
        {
            throw new DomainRuleException("media.audio_not_ready", "That file isn't a finished audio upload.");
        }

        AudioAssetId = asset.Id;
        AudioDurationSeconds = asset.DurationSeconds;
        UpdatedAt = now;
    }

    public void RemoveAudio(DateTimeOffset now)
    {
        EnsureEditable();
        if (IsPublic && Video is null)
        {
            throw new DomainRuleException("media.needs_media", "A published sermon needs audio or video. Add a video before removing the audio.");
        }

        AudioAssetId = null;
        AudioDurationSeconds = null;
        UpdatedAt = now;
    }

    public void AttachNotesPdf(MediaAsset? asset, DateTimeOffset now)
    {
        EnsureEditable();
        if (asset is not null && (asset.Kind != MediaKind.NotesPdf || asset.Status != MediaAssetStatus.Ready))
        {
            throw new DomainRuleException("media.pdf_not_ready", "That file isn't a finished PDF upload.");
        }

        NotesPdfAssetId = asset?.Id;
        UpdatedAt = now;
    }

    /// <summary>Sets the transcript by hand (or from captions), or clears it when <paramref name="text"/> is empty.</summary>
    public void SetTranscript(string? text, TranscriptSource source, DateTimeOffset now)
    {
        if (TranscriptStatus == TranscriptStatus.Working)
        {
            throw new DomainRuleException("media.transcribing", "The audio is being transcribed right now. Wait for it to finish.");
        }

        var trimmed = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        if (trimmed is { Length: > MaxTranscriptLength })
        {
            throw new DomainRuleException("media.too_long", $"The transcript is too long ({MaxTranscriptLength:N0} characters at most).");
        }

        Transcript = trimmed;
        TranscriptSource = trimmed is null ? null : source;
        TranscriptStatus = trimmed is null ? TranscriptStatus.None : TranscriptStatus.Ready;
        TranscriptError = null;
        TranscriptUpdatedAt = now;
    }

    /// <summary>Asks for the audio to be transcribed by the background job.</summary>
    public void QueueTranscription(DateTimeOffset now)
    {
        if (AudioAssetId is null)
        {
            throw new DomainRuleException("media.no_audio", "Upload the sermon's audio first.");
        }

        if (TranscriptStatus is TranscriptStatus.Queued or TranscriptStatus.Working)
        {
            return;
        }

        TranscriptStatus = TranscriptStatus.Queued;
        TranscriptError = null;
        TranscriptUpdatedAt = now;
    }

    public void StartTranscription(DateTimeOffset now)
    {
        if (TranscriptStatus != TranscriptStatus.Queued)
        {
            throw new DomainRuleException("media.not_queued", "This sermon isn't waiting to be transcribed.");
        }

        TranscriptStatus = TranscriptStatus.Working;
        TranscriptUpdatedAt = now;
    }

    public void CompleteTranscription(string text, DateTimeOffset now)
    {
        TranscriptStatus = TranscriptStatus.None;
        SetTranscript(text.Length > MaxTranscriptLength ? text[..MaxTranscriptLength] : text, Domain.TranscriptSource.Audio, now);
    }

    public void FailTranscription(string reason, DateTimeOffset now)
    {
        TranscriptStatus = TranscriptStatus.Failed;
        TranscriptError = reason.Length > 300 ? reason[..300] : reason;
        TranscriptUpdatedAt = now;
    }

    public void RefreshSearchText(IEnumerable<string> speakerNames, string? seriesTitle)
    {
        var parts = new[] { Title, Summary, seriesTitle }
            .Concat(speakerNames)
            .Concat(_scripture.Select(s => s.ToString()))
            .Concat(_scripture.Select(s => s.Book.Name))
            .Concat(Topics);
        SearchText = string.Join(' ', parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    /// <summary>The problems that stop this sermon being published. Empty when it's ready.</summary>
    public IReadOnlyList<string> PublishProblems()
    {
        var problems = new List<string>();
        if (_speakers.Count == 0)
        {
            problems.Add("Add at least one speaker.");
        }

        if (Video is null && AudioAssetId is null)
        {
            problems.Add("Add audio or a video.");
        }

        return problems;
    }

    public void Publish(DateTimeOffset now)
    {
        EnsurePublishable();
        Status = SermonStatus.Published;
        PublishAt = null;
        PublishedAt ??= now;
        UpdatedAt = now;
        Raise(new SermonPublished(Id, Title, Slug, Scope));
    }

    public void Schedule(DateTimeOffset publishAt, DateTimeOffset now)
    {
        EnsurePublishable();
        if (publishAt <= now)
        {
            throw new DomainRuleException("media.schedule_in_past", "Choose a publish time in the future, or publish now.");
        }

        Status = SermonStatus.Scheduled;
        PublishAt = publishAt;
        UpdatedAt = now;
    }

    /// <summary>Called by the scheduler. Publishes when the time has come; otherwise does nothing.</summary>
    public bool PublishIfDue(DateTimeOffset now)
    {
        if (Status != SermonStatus.Scheduled || PublishAt > now)
        {
            return false;
        }

        Publish(now);
        return true;
    }

    public void Unpublish(DateTimeOffset now)
    {
        if (Status is not (SermonStatus.Published or SermonStatus.Scheduled))
        {
            return;
        }

        Status = SermonStatus.Draft;
        PublishAt = null;
        UpdatedAt = now;
    }

    public void Archive(DateTimeOffset now)
    {
        Status = SermonStatus.Archived;
        PublishAt = null;
        UpdatedAt = now;
    }

    public void Restore(DateTimeOffset now)
    {
        if (Status == SermonStatus.Archived)
        {
            Status = SermonStatus.Draft;
            UpdatedAt = now;
        }
    }

    private void EnsurePublishable()
    {
        if (Status == SermonStatus.Archived)
        {
            throw new DomainRuleException("media.archived", "Restore this sermon before publishing it.");
        }

        var problems = PublishProblems();
        if (problems.Count > 0)
        {
            throw new DomainRuleException("media.not_ready", string.Join(" ", problems));
        }
    }

    private void EnsureEditable()
    {
        if (Status == SermonStatus.Archived)
        {
            throw new DomainRuleException("media.archived", "Restore this sermon before editing it.");
        }
    }

    private void SetTitle(string title)
    {
        var trimmed = title?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > 200)
        {
            throw new DomainRuleException("media.title_invalid", "A sermon title is required (200 characters at most).");
        }

        Title = trimmed;
    }

    private static string? Clean(string? value, int max, string field)
    {
        var trimmed = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return trimmed is { Length: var length } && length > max
            ? throw new DomainRuleException("media.too_long", $"The {field} is too long ({max} characters at most).")
            : trimmed;
    }

    /// <summary>"2026-09-27-deep-calls-unto-deep". The date keeps repeated titles unique and links readable.</summary>
    public static string Slugify(string title, DateOnly preachedOn) => $"{preachedOn:yyyy-MM-dd}-{SlugWords(title, "sermon")}";

    /// <summary>Lower-case words joined by dashes, at most 60 characters: "Deep calls unto deep" gives "deep-calls-unto-deep".</summary>
    public static string SlugWords(string text, string fallback)
    {
        var words = NonSlug().Replace(text.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD), "-").Trim('-');
        words = MultiDash().Replace(words, "-");
        if (words.Length > 60)
        {
            words = words[..60].TrimEnd('-');
        }

        return words.Length == 0 ? fallback : words;
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonSlug();

    [GeneratedRegex("-{2,}")]
    private static partial Regex MultiDash();
}

public sealed class SermonSpeaker
{
    private SermonSpeaker()
    {
    }

    internal SermonSpeaker(Guid speakerId, int order)
    {
        SpeakerId = speakerId;
        Order = order;
    }

    public Guid SpeakerId { get; private set; }

    public int Order { get; private set; }
}
