namespace Shapers.Prayer.Domain;

/// <summary>Who may see a request. Group visibility arrives with the Groups module.</summary>
public enum PrayerVisibility
{
    /// <summary>On the members' prayer wall once a reviewer approves it.</summary>
    Wall,

    /// <summary>Only people with prayer.requests.view. Never on the wall.</summary>
    PastorsOnly,
}

public enum PrayerStatus
{
    /// <summary>Asked for the wall; waiting for a reviewer.</summary>
    AwaitingReview,

    /// <summary>Showing on the wall.</summary>
    OnWall,

    /// <summary>With the pastors only: asked for that, or not approved for the wall.</summary>
    WithPastors,

    /// <summary>Withdrawn by the requester, taken down, or past its time on the wall. Pastors still see it until retention deletes it.</summary>
    Closed,
}

public enum PrayerSource
{
    App,
    ConnectCard,
}

public sealed record PrayerRequestSubmitted(Guid RequestId, Guid PersonId, string Scope, PrayerVisibility Visibility) : IDomainEvent;

public sealed record PrayerRequestApproved(Guid RequestId, Guid PersonId) : IDomainEvent;

/// <summary>
/// A prayer request. Religious and often health information, so it is special personal information under POPIA:
/// the text is never put in notifications, and reads by pastors are audited.
/// </summary>
public sealed class PrayerRequest : AggregateRoot<Guid>
{
    public const int MaxLength = 1000;

    /// <summary>How long an approved request stays on the wall.</summary>
    public static readonly TimeSpan WallDuration = TimeSpan.FromDays(30);

    private PrayerRequest()
    {
    }

    public Guid PersonId { get; private set; }

    /// <summary>The requester's scope when they asked, so the right campus pastors see it.</summary>
    public string Scope { get; private set; } = null!;

    /// <summary>What the requester wrote. Only the requester and pastors see this.</summary>
    public string Text { get; private set; } = null!;

    /// <summary>What the wall shows: the reviewer may remove third-party names or health details.</summary>
    public string? WallText { get; private set; }

    public PrayerVisibility Visibility { get; private set; }

    /// <summary>Hide the requester's name on the wall. Pastors always see who asked.</summary>
    public bool Anonymous { get; private set; }

    public PrayerStatus Status { get; private set; }

    public PrayerSource Source { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid? ReviewedByUserId { get; private set; }

    public DateTimeOffset? ReviewedAt { get; private set; }

    /// <summary>Shown to the requester when a request isn't approved for the wall.</summary>
    public string? ReviewNote { get; private set; }

    public DateTimeOffset? WallUntil { get; private set; }

    public DateTimeOffset? AnsweredAt { get; private set; }

    public string? AnswerNote { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public static PrayerRequest Submit(Guid personId, ScopePath scope, string text, PrayerVisibility visibility, bool anonymous, PrayerSource source, DateTimeOffset now)
    {
        var request = new PrayerRequest
        {
            Id = Guid.CreateVersion7(),
            PersonId = personId,
            Scope = scope.Value,
            Text = Clean(text),
            Visibility = visibility,
            Anonymous = anonymous,
            Status = visibility == PrayerVisibility.Wall ? PrayerStatus.AwaitingReview : PrayerStatus.WithPastors,
            Source = source,
            CreatedAt = now,
        };
        request.Raise(new PrayerRequestSubmitted(request.Id, personId, request.Scope, visibility));
        return request;
    }

    /// <summary>A reviewer puts the request on the wall, optionally with wording that protects other people's privacy.</summary>
    public void Approve(string? wallText, Guid reviewerUserId, DateTimeOffset now)
    {
        if (Status != PrayerStatus.AwaitingReview)
        {
            throw new DomainRuleException("prayer.not_awaiting_review", "This request isn't waiting for review.");
        }

        WallText = string.IsNullOrWhiteSpace(wallText) ? Text : Clean(wallText);
        Status = PrayerStatus.OnWall;
        ReviewedByUserId = reviewerUserId;
        ReviewedAt = now;
        WallUntil = now + WallDuration;
        Raise(new PrayerRequestApproved(Id, PersonId));
    }

    /// <summary>Not suitable for the wall: it stays with the pastors, and the requester is told why.</summary>
    public void KeepWithPastors(string? note, Guid reviewerUserId, DateTimeOffset now)
    {
        if (Status != PrayerStatus.AwaitingReview)
        {
            throw new DomainRuleException("prayer.not_awaiting_review", "This request isn't waiting for review.");
        }

        Status = PrayerStatus.WithPastors;
        ReviewNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        ReviewedByUserId = reviewerUserId;
        ReviewedAt = now;
    }

    /// <summary>A reviewer takes a request off the wall, e.g. after a report.</summary>
    public void TakeDown(Guid reviewerUserId, DateTimeOffset now)
    {
        if (Status != PrayerStatus.OnWall)
        {
            throw new DomainRuleException("prayer.not_on_wall", "This request isn't on the wall.");
        }

        ReviewedByUserId = reviewerUserId;
        ReviewedAt = now;
        Close(now);
    }

    public void MarkAnswered(string? note, DateTimeOffset now)
    {
        AnsweredAt ??= now;
        if (!string.IsNullOrWhiteSpace(note))
        {
            AnswerNote = Clean(note);
        }
    }

    public void Withdraw(DateTimeOffset now)
    {
        if (Status != PrayerStatus.Closed)
        {
            Close(now);
        }
    }

    /// <summary>Called by the nightly job; true when the request left the wall.</summary>
    public bool ExpireFromWall(DateTimeOffset now)
    {
        if (Status != PrayerStatus.OnWall || WallUntil > now)
        {
            return false;
        }

        Close(now);
        return true;
    }

    /// <summary>The name the wall shows.</summary>
    public string WallName(string displayName) => Anonymous ? "Someone from Shapers" : FirstNameOnly(displayName);

    private void Close(DateTimeOffset now)
    {
        Status = PrayerStatus.Closed;
        ClosedAt = now;
    }

    // Members see only first names on the wall: enough to pray for someone, less to identify them.
    private static string FirstNameOnly(string displayName)
    {
        var name = displayName.Trim();
        var space = name.IndexOf(' ', StringComparison.Ordinal);
        return space > 0 ? name[..space] : name;
    }

    private static string Clean(string text)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw new DomainRuleException("prayer.text_required", "Write your prayer request.");
        }

        if (trimmed.Length > MaxLength)
        {
            throw new DomainRuleException("prayer.text_too_long", $"Keep it under {MaxLength} characters.");
        }

        return trimmed;
    }
}

/// <summary>"I prayed": one per person per request.</summary>
public sealed class PrayerResponse
{
    private PrayerResponse()
    {
    }

    public Guid RequestId { get; private set; }

    public Guid PersonId { get; private set; }

    public DateTimeOffset PrayedAt { get; private set; }

    public static PrayerResponse Record(Guid requestId, Guid personId, DateTimeOffset now) =>
        new() { RequestId = requestId, PersonId = personId, PrayedAt = now };
}
