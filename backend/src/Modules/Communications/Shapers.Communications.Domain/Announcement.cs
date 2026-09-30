namespace Shapers.Communications.Domain;

public enum AnnouncementStatus
{
    Draft,

    /// <summary>Wide audience: waiting for a second person to approve it.</summary>
    AwaitingApproval,

    /// <summary>Ready; goes out at SendAt (or straight away).</summary>
    Queued,

    Sent,
    Cancelled,
}

/// <summary>
/// A message from the church to a scope (the whole church, a campus or a ministry). Anything wider than a ministry
/// needs a second person to approve it before it goes out, so one mistake can't reach everyone.
/// </summary>
public sealed class Announcement : AggregateRoot<Guid>
{
    public const int MaxTitle = 120;
    public const int MaxBody = 2000;

    private Announcement()
    {
    }

    public string Title { get; private set; } = null!;

    public string Body { get; private set; } = null!;

    /// <summary>Optional place in the app to open, e.g. "/event/2026-10-family-fun-day".</summary>
    public string? Link { get; private set; }

    public string Scope { get; private set; } = null!;

    public bool SendEmail { get; private set; }

    /// <summary>Null means as soon as it is approved (or submitted, when no approval is needed).</summary>
    public DateTimeOffset? SendAt { get; private set; }

    public AnnouncementStatus Status { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public Guid? ApprovedByUserId { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }

    /// <summary>Why an approver sent it back.</summary>
    public string? ReturnNote { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    public int Recipients { get; private set; }

    /// <summary>The whole church or a whole campus needs a second approver; a ministry doesn't.</summary>
    public bool NeedsApproval => ScopePath.Parse(Scope).Type is ScopeType.Global or ScopeType.Campus;

    public static Announcement Draft(string title, string body, string? link, ScopePath scope, bool sendEmail, DateTimeOffset? sendAt, Guid authorUserId, DateTimeOffset now)
    {
        var announcement = new Announcement { Id = Guid.CreateVersion7(), CreatedByUserId = authorUserId, CreatedAt = now, Status = AnnouncementStatus.Draft };
        announcement.Edit(title, body, link, scope, sendEmail, sendAt, now);
        return announcement;
    }

    public void Edit(string title, string body, string? link, ScopePath scope, bool sendEmail, DateTimeOffset? sendAt, DateTimeOffset now)
    {
        if (Status != AnnouncementStatus.Draft)
        {
            throw new DomainRuleException("communications.not_draft", "Only drafts can be changed. Send it back to draft first.");
        }

        Title = Required(title, MaxTitle, "Give it a title.");
        Body = Required(body, MaxBody, "Write the message.");
        if (!string.IsNullOrWhiteSpace(link) && !link.Trim().StartsWith('/'))
        {
            throw new DomainRuleException("communications.link_invalid", "Links must be places in the app, e.g. /event/family-fun-day.");
        }

        Link = string.IsNullOrWhiteSpace(link) ? null : link.Trim();
        Scope = scope.Value;
        SendEmail = sendEmail;
        SendAt = sendAt;
        UpdatedAt = now;
    }

    /// <summary>The author is done: it goes for approval, or straight to the queue for a ministry.</summary>
    public void Submit(DateTimeOffset now)
    {
        if (Status != AnnouncementStatus.Draft)
        {
            throw new DomainRuleException("communications.not_draft", "This announcement has already been submitted.");
        }

        if (SendAt is { } at && at < now.AddMinutes(-5))
        {
            throw new DomainRuleException("communications.send_at_past", "The send time has passed. Choose a new time or send now.");
        }

        ReturnNote = null;
        Status = NeedsApproval ? AnnouncementStatus.AwaitingApproval : AnnouncementStatus.Queued;
        UpdatedAt = now;
    }

    public void Approve(Guid approverUserId, DateTimeOffset now)
    {
        if (Status != AnnouncementStatus.AwaitingApproval)
        {
            throw new DomainRuleException("communications.not_awaiting_approval", "This announcement isn't waiting for approval.");
        }

        if (approverUserId == CreatedByUserId)
        {
            throw new DomainRuleException("communications.own_announcement", "Someone other than the author must approve it.");
        }

        ApprovedByUserId = approverUserId;
        ApprovedAt = now;
        Status = AnnouncementStatus.Queued;
        UpdatedAt = now;
    }

    /// <summary>Back to draft, from approval or from the queue before it is sent.</summary>
    public void ReturnToDraft(string? note, DateTimeOffset now)
    {
        if (Status is not (AnnouncementStatus.AwaitingApproval or AnnouncementStatus.Queued))
        {
            throw new DomainRuleException("communications.cannot_return", "Only announcements that haven't gone out can go back to draft.");
        }

        Status = AnnouncementStatus.Draft;
        ReturnNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        ApprovedByUserId = null;
        ApprovedAt = null;
        UpdatedAt = now;
    }

    public void Cancel(DateTimeOffset now)
    {
        if (Status is AnnouncementStatus.Sent or AnnouncementStatus.Cancelled)
        {
            throw new DomainRuleException("communications.cannot_cancel", "This announcement has already gone out or been cancelled.");
        }

        Status = AnnouncementStatus.Cancelled;
        UpdatedAt = now;
    }

    public bool IsDue(DateTimeOffset now) => Status == AnnouncementStatus.Queued && (SendAt is null || SendAt <= now);

    public void MarkSent(int recipients, DateTimeOffset now)
    {
        Status = AnnouncementStatus.Sent;
        SentAt = now;
        Recipients = recipients;
        UpdatedAt = now;
    }

    public string SourceKey => $"announcement:{Id}";

    private static string Required(string value, int max, string message)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw new DomainRuleException("communications.required", message);
        }

        if (trimmed.Length > max)
        {
            throw new DomainRuleException("communications.too_long", $"Keep it under {max} characters.");
        }

        return trimmed;
    }
}
