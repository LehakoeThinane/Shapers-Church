namespace Shapers.Media.Domain;

public enum LivestreamStatus
{
    Scheduled,
    Live,
    Ended,
    Cancelled,
}

public sealed record LivestreamStarted(Guid LivestreamId, string Title, string Scope) : IDomainEvent;

public sealed record LivestreamEnded(Guid LivestreamId) : IDomainEvent;

/// <summary>
/// A streamed service. Video is YouTube Live; we hold the link, the notes, and the scripture shown on
/// screen during the service. A producer moves it from Scheduled to Live to Ended by hand, which is
/// reliable and needs no YouTube API quota.
/// </summary>
public sealed class Livestream : AggregateRoot<Guid>
{
    public const int MaxCues = 60;

    private readonly List<ScriptureCue> _cues = [];

    private Livestream()
    {
    }

    public string Title { get; private set; } = null!;

    public string Scope { get; private set; } = null!;

    public DateTimeOffset ScheduledStart { get; private set; }

    public LivestreamStatus Status { get; private set; }

    public VideoLink? Video { get; private set; }

    /// <summary>Sermon notes for the Notes tab, in Markdown.</summary>
    public string? Notes { get; private set; }

    /// <summary>Where the Give button goes until in-app giving exists.</summary>
    public string? GiveUrl { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? EndedAt { get; private set; }

    public Guid? CurrentCueId { get; private set; }

    /// <summary>The draft sermon made from this service, once someone has done so.</summary>
    public Guid? SermonId { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Slow mode: how long each person waits between chat messages.</summary>
    public int ChatSlowSeconds { get; private set; } = ChatRules.DefaultSlowSeconds;

    /// <summary>When on, every member's message waits for a moderator before anyone sees it.</summary>
    public bool ChatApprovalRequired { get; private set; }

    public IReadOnlyList<ScriptureCue> Cues => _cues;

    /// <summary>
    /// Chat opens 15 minutes before the scheduled start and closes 30 minutes after the service ends. A service
    /// nobody marked live stops taking messages three hours after its start time.
    /// </summary>
    public bool IsChatOpen(DateTimeOffset now) => Status switch
    {
        LivestreamStatus.Live => true,
        LivestreamStatus.Scheduled => now >= ScheduledStart - ChatRules.OpensBeforeStart && now <= ScheduledStart + ChatRules.UnstartedLimit,
        LivestreamStatus.Ended => EndedAt is { } ended && now <= ended + ChatRules.ClosesAfterEnd,
        _ => false,
    };

    public void SetChatRules(int slowSeconds, bool approvalRequired, DateTimeOffset now)
    {
        if (slowSeconds < ChatRules.DefaultSlowSeconds || slowSeconds > ChatRules.MaxSlowSeconds)
        {
            throw new DomainRuleException("media.chat_slow_mode", $"Slow mode can be between {ChatRules.DefaultSlowSeconds} and {ChatRules.MaxSlowSeconds} seconds.");
        }

        ChatSlowSeconds = slowSeconds;
        ChatApprovalRequired = approvalRequired;
        UpdatedAt = now;
    }

    public ScriptureCue? CurrentCue => _cues.SingleOrDefault(c => c.Id == CurrentCueId);

    public static Livestream Schedule(string title, ScopePath scope, DateTimeOffset scheduledStart, DateTimeOffset now)
    {
        var stream = new Livestream { Id = Guid.CreateVersion7(), Scope = scope.Value, Status = LivestreamStatus.Scheduled };
        stream.Update(title, scheduledStart, null, null, null, now);
        return stream;
    }

    public void Update(string title, DateTimeOffset scheduledStart, VideoLink? video, string? notes, string? giveUrl, DateTimeOffset now)
    {
        EnsureNotFinished();
        var trimmed = title?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > 150)
        {
            throw new DomainRuleException("media.stream_title", "A stream needs a title (150 characters at most).");
        }

        if (giveUrl is not null && !(Uri.TryCreate(giveUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps))
        {
            throw new DomainRuleException("media.give_url", "The Give link must be a full https:// address.");
        }

        if (notes is { Length: > 20_000 })
        {
            throw new DomainRuleException("media.too_long", "The notes are too long (20,000 characters at most).");
        }

        Title = trimmed;
        ScheduledStart = scheduledStart;
        Video = video;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        GiveUrl = string.IsNullOrWhiteSpace(giveUrl) ? null : giveUrl.Trim();
        UpdatedAt = now;
    }

    public void GoLive(DateTimeOffset now)
    {
        if (Status == LivestreamStatus.Live)
        {
            return;
        }

        EnsureNotFinished();
        if (Video is null)
        {
            throw new DomainRuleException("media.stream_no_video", "Add the YouTube Live link before going live.");
        }

        Status = LivestreamStatus.Live;
        StartedAt = now;
        UpdatedAt = now;
        Raise(new LivestreamStarted(Id, Title, Scope));
    }

    public void End(DateTimeOffset now)
    {
        if (Status != LivestreamStatus.Live)
        {
            throw new DomainRuleException("media.stream_not_live", "Only a live stream can be ended.");
        }

        Status = LivestreamStatus.Ended;
        EndedAt = now;
        CurrentCueId = null;
        UpdatedAt = now;
        Raise(new LivestreamEnded(Id));
    }

    public void Cancel(DateTimeOffset now)
    {
        if (Status != LivestreamStatus.Scheduled)
        {
            throw new DomainRuleException("media.stream_cannot_cancel", "Only a stream that hasn't started can be cancelled.");
        }

        Status = LivestreamStatus.Cancelled;
        UpdatedAt = now;
    }

    public ScriptureCue AddCue(ScriptureReference reference, string? verseText, DateTimeOffset now)
    {
        EnsureNotFinished();
        if (_cues.Count >= MaxCues)
        {
            throw new DomainRuleException("media.too_many_cues", $"A service can have at most {MaxCues} scripture slides.");
        }

        var cue = new ScriptureCue(reference.ToString(), string.IsNullOrWhiteSpace(verseText) ? null : verseText.Trim(), _cues.Count);
        _cues.Add(cue);
        UpdatedAt = now;
        return cue;
    }

    public void RemoveCue(Guid cueId, DateTimeOffset now)
    {
        EnsureNotFinished();
        _cues.RemoveAll(c => c.Id == cueId);
        if (CurrentCueId == cueId)
        {
            CurrentCueId = null;
        }

        UpdatedAt = now;
    }

    /// <summary>Puts a passage "on screen" for everyone watching. Null clears the screen.</summary>
    public void ShowCue(Guid? cueId, DateTimeOffset now)
    {
        if (Status != LivestreamStatus.Live)
        {
            throw new DomainRuleException("media.stream_not_live", "Scripture can only be shown while the stream is live.");
        }

        if (cueId is { } id && _cues.All(c => c.Id != id))
        {
            throw new DomainRuleException("media.cue_not_found", "That scripture slide isn't part of this service.");
        }

        CurrentCueId = cueId;
        _cues.SingleOrDefault(c => c.Id == cueId)?.MarkShown(now);
        UpdatedAt = now;
    }

    public void LinkSermon(Guid sermonId, DateTimeOffset now)
    {
        if (SermonId is not null)
        {
            throw new DomainRuleException("media.stream_has_sermon", "A sermon was already made from this service.");
        }

        SermonId = sermonId;
        UpdatedAt = now;
    }

    private void EnsureNotFinished()
    {
        if (Status is LivestreamStatus.Ended or LivestreamStatus.Cancelled)
        {
            throw new DomainRuleException("media.stream_finished", "This stream has finished and can't be changed.");
        }
    }
}

public sealed class ScriptureCue
{
    private ScriptureCue()
    {
    }

    internal ScriptureCue(string reference, string? text, int order)
    {
        Id = Guid.CreateVersion7();
        Reference = reference;
        Text = text;
        Order = order;
    }

    public Guid Id { get; private set; }

    public string Reference { get; private set; } = null!;

    /// <summary>Verse text as shown (World English Bible until a licensed translation is agreed).</summary>
    public string? Text { get; private set; }

    public int Order { get; private set; }

    public DateTimeOffset? ShownAt { get; private set; }

    internal void MarkShown(DateTimeOffset at) => ShownAt ??= at;
}
