using System.Text.RegularExpressions;

namespace Shapers.Media.Domain;

public enum ChatMessageStatus
{
    Visible,

    /// <summary>Waiting for a moderator: only the author and moderators can see it.</summary>
    Held,

    Hidden,
}

public enum ChatHoldReason
{
    /// <summary>The chat was switched to "moderators approve every message".</summary>
    Approval,

    WordList,

    Reports,
}

public enum ChatSanctionKind
{
    /// <summary>Can't post for the rest of this service.</summary>
    Timeout,

    /// <summary>Can't post in any service until a moderator lifts it.</summary>
    Ban,
}

public sealed record ChatMessageReported(Guid MessageId, Guid LivestreamId, string Scope) : IDomainEvent;

/// <summary>The rules every livestream chat follows (see the B2 plan).</summary>
public static class ChatRules
{
    public const int MaxLength = 300;
    public const int DefaultSlowSeconds = 5;
    public const int MaxSlowSeconds = 300;

    /// <summary>Reports from this many different people hide a message until a moderator looks at it.</summary>
    public const int ReportsToHold = 3;

    public static readonly TimeSpan OpensBeforeStart = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan ClosesAfterEnd = TimeSpan.FromMinutes(30);

    /// <summary>A service that was never marked live stops accepting messages this long after its start time.</summary>
    public static readonly TimeSpan UnstartedLimit = TimeSpan.FromHours(3);

    /// <summary>Ordinary messages.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(90);

    /// <summary>Hidden or reported messages and moderation actions: kept as evidence if there is a safeguarding concern.</summary>
    public static readonly TimeSpan EvidenceRetention = TimeSpan.FromDays(365);

    /// <summary>"Thabo Mokoena" becomes "Thabo M.": enough to know who is talking, no more.</summary>
    public static string ShortName(string displayName)
    {
        var parts = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            0 => "Someone",
            1 => parts[0],
            _ => $"{parts[0]} {char.ToUpperInvariant(parts[^1][0])}.",
        };
    }

    /// <summary>True when the text contains a listed word or phrase as a whole word, ignoring case.</summary>
    public static bool MatchesWordList(string text, IEnumerable<string> terms) =>
        terms.Any(term => !string.IsNullOrWhiteSpace(term)
            && Regex.IsMatch(text, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(term.Trim())}(?![\p{{L}}\p{{N}}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)));
}

/// <summary>
/// One message in a livestream's chat. The name is stored as shown at the time ("Thabo M."), so the chat reads the
/// same to moderators later as it did to everyone watching.
/// </summary>
public sealed class ChatMessage : AggregateRoot<Guid>
{
    private readonly List<ChatReport> _reports = [];

    private ChatMessage()
    {
    }

    public Guid LivestreamId { get; private set; }

    /// <summary>The livestream's scope, so moderators for that campus are the ones alerted.</summary>
    public string Scope { get; private set; } = null!;

    public Guid PersonId { get; private set; }

    public string AuthorName { get; private set; } = null!;

    /// <summary>Shown with a badge: the author moderates this chat.</summary>
    public bool FromTeam { get; private set; }

    public string Text { get; private set; } = null!;

    public DateTimeOffset SentAt { get; private set; }

    public ChatMessageStatus Status { get; private set; }

    public ChatHoldReason? HoldReason { get; private set; }

    public Guid? ModeratedBy { get; private set; }

    public DateTimeOffset? ModeratedAt { get; private set; }

    public IReadOnlyList<ChatReport> Reports => _reports;

    public static ChatMessage Post(
        Guid livestreamId,
        string scope,
        Guid personId,
        string authorName,
        bool fromTeam,
        string text,
        ChatHoldReason? hold,
        DateTimeOffset now)
    {
        var clean = Clean(text);
        return new ChatMessage
        {
            Id = Guid.CreateVersion7(),
            LivestreamId = livestreamId,
            Scope = scope,
            PersonId = personId,
            AuthorName = authorName,
            FromTeam = fromTeam,
            Text = clean,
            SentAt = now,
            Status = hold is null ? ChatMessageStatus.Visible : ChatMessageStatus.Held,
            HoldReason = hold,
        };
    }

    /// <summary>Records a report once per person. Returns true when this report newly held the message back.</summary>
    public bool Report(Guid reporterId, DateTimeOffset now)
    {
        if (reporterId == PersonId)
        {
            throw new DomainRuleException("media.chat_own_message", "You can't report your own message.");
        }

        if (_reports.Any(r => r.ReporterId == reporterId))
        {
            return false;
        }

        _reports.Add(new ChatReport(reporterId, now));
        if (_reports.Count == 1)
        {
            Raise(new ChatMessageReported(Id, LivestreamId, Scope));
        }

        // A moderator's decision stands: once they have restored a message, reports don't hide it again.
        if (Status == ChatMessageStatus.Visible && ModeratedBy is null && _reports.Count >= ChatRules.ReportsToHold)
        {
            Status = ChatMessageStatus.Held;
            HoldReason = ChatHoldReason.Reports;
            return true;
        }

        return false;
    }

    /// <summary>Erasure: the reporter's identity goes; the moderation decision it led to stays.</summary>
    public void ForgetReporter(Guid reporterId) => _reports.RemoveAll(r => r.ReporterId == reporterId);

    public void Hide(Guid moderatorId, DateTimeOffset now)
    {
        Status = ChatMessageStatus.Hidden;
        ModeratedBy = moderatorId;
        ModeratedAt = now;
    }

    /// <summary>Approves a held message or undoes a hide.</summary>
    public void Show(Guid moderatorId, DateTimeOffset now)
    {
        Status = ChatMessageStatus.Visible;
        HoldReason = null;
        ModeratedBy = moderatorId;
        ModeratedAt = now;
    }

    /// <summary>Hidden, held or reported messages are kept longer, as evidence.</summary>
    public bool IsEvidence => Status != ChatMessageStatus.Visible || _reports.Count > 0 || ModeratedBy is not null;

    private static string Clean(string? text)
    {
        // Control and text-direction characters can hide or reorder what people read; they have no place in chat.
        // (Joiners stay: emoji need them.)
        var chars = (text ?? string.Empty).Where(c => (!char.IsControl(c) || c == '\n') && !IsDirectionControl(c)).ToArray();
        var clean = new string(chars).Trim();
        while (clean.Contains("\n\n\n", StringComparison.Ordinal))
        {
            clean = clean.Replace("\n\n\n", "\n\n", StringComparison.Ordinal);
        }

        if (clean.Length == 0)
        {
            throw new DomainRuleException("media.chat_empty", "Type a message first.");
        }

        if (clean.Length > ChatRules.MaxLength)
        {
            throw new DomainRuleException("media.chat_too_long", $"Messages can be {ChatRules.MaxLength} characters at most.");
        }

        return clean;
    }

    private static bool IsDirectionControl(char c) => c is '‎' or '‏' or (>= '‪' and <= '‮') or (>= '⁦' and <= '⁩');
}

public sealed class ChatReport
{
    private ChatReport()
    {
    }

    internal ChatReport(Guid reporterId, DateTimeOffset reportedAt)
    {
        ReporterId = reporterId;
        ReportedAt = reportedAt;
    }

    public Guid ReporterId { get; private set; }

    public DateTimeOffset ReportedAt { get; private set; }
}

/// <summary>A timeout (this service) or a ban (every service) on someone posting. Lifting keeps the record.</summary>
public sealed class ChatSanction : Entity<Guid>
{
    private ChatSanction()
    {
    }

    public Guid PersonId { get; private set; }

    public ChatSanctionKind Kind { get; private set; }

    /// <summary>The service a timeout applies to (and where a ban was given).</summary>
    public Guid LivestreamId { get; private set; }

    public string AuthorName { get; private set; } = null!;

    public string? Reason { get; private set; }

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid? LiftedBy { get; private set; }

    public DateTimeOffset? LiftedAt { get; private set; }

    public bool IsActive => LiftedAt is null;

    public static ChatSanction Impose(Guid personId, ChatSanctionKind kind, Guid livestreamId, string authorName, string? reason, Guid moderatorId, DateTimeOffset now)
    {
        if (personId == moderatorId)
        {
            throw new DomainRuleException("media.chat_self_sanction", "You can't time out or ban yourself.");
        }

        if (reason is { Length: > 300 })
        {
            throw new DomainRuleException("media.too_long", "Keep the reason under 300 characters.");
        }

        return new ChatSanction
        {
            Id = Guid.CreateVersion7(),
            PersonId = personId,
            Kind = kind,
            LivestreamId = livestreamId,
            AuthorName = authorName,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            CreatedBy = moderatorId,
            CreatedAt = now,
        };
    }

    public bool AppliesTo(Guid livestreamId) => IsActive && (Kind == ChatSanctionKind.Ban || LivestreamId == livestreamId);

    public void Lift(Guid moderatorId, DateTimeOffset now)
    {
        if (!IsActive)
        {
            return;
        }

        LiftedBy = moderatorId;
        LiftedAt = now;
    }
}

/// <summary>A word or phrase that holds a message for a moderator instead of showing it.</summary>
public sealed class ChatBlockedTerm
{
    private ChatBlockedTerm()
    {
    }

    public ChatBlockedTerm(string term)
    {
        Term = term.Trim().ToLowerInvariant();
    }

    public string Term { get; private set; } = null!;
}
