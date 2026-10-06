namespace Shapers.Groups.Domain;

public enum ReportStatus
{
    /// <summary>Being written; only the cell's leaders see it.</summary>
    Draft,

    /// <summary>Sent to the pastors. No longer editable.</summary>
    Submitted,
}

/// <summary>How close the cell is to growing into two.</summary>
public enum MultiplicationReadiness
{
    NotYet,
    Growing,
    Ready,
}

public enum NextStep
{
    Baptism,
    GrowthTrack,
    Serving,
    Leadership,
}

/// <summary>A visitor. Without their agreement to be contacted only a first name is kept, and it never leaves the report.</summary>
public sealed record ReportVisitor(string FirstName, string? LastName, string? Mobile, string? Email, bool AgreedToBeContacted);

/// <summary>Someone the leader wants followed up. Urgent ones alert the pastors as soon as the report is submitted.</summary>
public sealed record ReportFollowUp(Guid Id, Guid? PersonId, string Name, string Note, bool Urgent, DateTimeOffset? ResolvedAt = null, Guid? ResolvedByUserId = null);

/// <summary>A member ready for a next step: baptism, the Growth Track, serving or leadership.</summary>
public sealed record ReportGrowth(Guid PersonId, NextStep Step);

/// <summary>What the leader writes; everything optional except the meeting date.</summary>
public sealed record ReportContent(
    DateOnly MeetingDate,
    string? Topic,
    Guid? MaterialId,
    string? Notes,
    string? Highlights,
    string? PrayerNeeds,
    MultiplicationReadiness Multiplication,
    IReadOnlyList<Guid> AttendeeIds,
    IReadOnlyList<ReportVisitor> Visitors,
    IReadOnlyList<ReportFollowUp> FollowUps,
    IReadOnlyList<ReportGrowth> Growth);

public sealed record CellReportSubmitted(Guid ReportId, Guid CellId, string CellName, string Scope, DateOnly MeetingDate, IReadOnlyList<ReportVisitor> VisitorsToContact, bool HasUrgentFollowUp) : IDomainEvent;

/// <summary>
/// A leader's report on one cell meeting. Prayer needs, follow-ups and notes can be special personal information
/// under POPIA, so only the cell's leaders and the pastors read it, pastors' reads are audited, and the personal
/// parts are deleted after a year (attendance numbers stay for trends).
/// </summary>
public sealed class CellReport : AggregateRoot<Guid>
{
    public const int MaxTextLength = 4000;
    public const int MaxItems = 60;

    /// <summary>How long the personal parts of a report are kept.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(365);

    private CellReport()
    {
    }

    public Guid CellId { get; private set; }

    /// <summary>Copied from the cell so pastors' access can be checked without loading it.</summary>
    public string Scope { get; private set; } = null!;

    public DateOnly MeetingDate { get; private set; }

    public ReportStatus Status { get; private set; }

    public string? Topic { get; private set; }

    public Guid? MaterialId { get; private set; }

    public string? Notes { get; private set; }

    public string? Highlights { get; private set; }

    public string? PrayerNeeds { get; private set; }

    public MultiplicationReadiness Multiplication { get; private set; }

    public List<Guid> AttendeeIds { get; private set; } = [];

    public List<ReportVisitor> Visitors { get; private set; } = [];

    public List<ReportFollowUp> FollowUps { get; private set; } = [];

    public List<ReportGrowth> Growth { get; private set; } = [];

    /// <summary>Kept after the personal details are removed, for attendance trends.</summary>
    public int MembersPresent { get; private set; }

    public int VisitorCount { get; private set; }

    public Guid WrittenByPersonId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? SubmittedAt { get; private set; }

    /// <summary>When retention removed the personal parts.</summary>
    public DateTimeOffset? RedactedAt { get; private set; }

    public bool HasOpenUrgentFollowUp => FollowUps.Any(f => f.Urgent && f.ResolvedAt is null);

    /// <summary>Starts a report. <paramref name="members"/> are the cell's current members: only they can be marked present.</summary>
    public static CellReport Start(Guid cellId, ScopePath scope, ReportContent content, IReadOnlyCollection<Guid> members, Guid writtenBy, DateTimeOffset now)
    {
        var report = new CellReport
        {
            Id = Guid.CreateVersion7(),
            CellId = cellId,
            Scope = scope.Value,
            Status = ReportStatus.Draft,
            WrittenByPersonId = writtenBy,
            CreatedAt = now,
        };
        report.Apply(content, members, now);
        return report;
    }

    public void Edit(ReportContent content, IReadOnlyCollection<Guid> members, DateTimeOffset now)
    {
        if (Status != ReportStatus.Draft)
        {
            throw new DomainRuleException("groups.report_submitted", "This report has been submitted and can't be changed.");
        }

        Apply(content, members, now);
    }

    /// <summary>Sends the report to the pastors. Visitors who agreed are passed on for follow-up; urgent follow-ups alert the pastors.</summary>
    public void Submit(string cellName, DateTimeOffset now)
    {
        if (Status != ReportStatus.Draft)
        {
            throw new DomainRuleException("groups.report_submitted", "This report has already been submitted.");
        }

        Status = ReportStatus.Submitted;
        SubmittedAt = now;
        UpdatedAt = now;
        Raise(new CellReportSubmitted(Id, CellId, cellName, Scope, MeetingDate, Visitors.Where(v => v.AgreedToBeContacted).ToList(), FollowUps.Any(f => f.Urgent)));
    }

    /// <summary>A pastor has dealt with a follow-up.</summary>
    public void ResolveFollowUp(Guid followUpId, Guid byUserId, DateTimeOffset now)
    {
        var index = FollowUps.FindIndex(f => f.Id == followUpId);
        if (index < 0)
        {
            throw new DomainRuleException("groups.follow_up_not_found", "Follow-up not found.");
        }

        if (FollowUps[index].ResolvedAt is null)
        {
            // Replace the list so the change to the stored JSON is noticed.
            FollowUps = FollowUps.Select((f, i) => i == index ? f with { ResolvedAt = now, ResolvedByUserId = byUserId } : f).ToList();
        }
    }

    /// <summary>Retention: removes who came and what was shared, keeping only the numbers.</summary>
    public bool Redact(DateTimeOffset now)
    {
        if (RedactedAt is not null)
        {
            return false;
        }

        Notes = null;
        Highlights = null;
        PrayerNeeds = null;
        AttendeeIds = [];
        Visitors = [];
        FollowUps = [];
        Growth = [];
        RedactedAt = now;
        return true;
    }

    /// <summary>Privacy erasure: removes one person from the report.</summary>
    public bool RemovePerson(Guid personId)
    {
        var before = AttendeeIds.Count + FollowUps.Count + Growth.Count;
        AttendeeIds = AttendeeIds.Where(id => id != personId).ToList();
        FollowUps = FollowUps.Where(f => f.PersonId != personId).ToList();
        Growth = Growth.Where(g => g.PersonId != personId).ToList();
        return AttendeeIds.Count + FollowUps.Count + Growth.Count != before;
    }

    /// <summary>A merged person's mentions now point at the surviving record.</summary>
    public void ReplacePerson(Guid mergedId, Guid survivorId)
    {
        AttendeeIds = AttendeeIds.Select(id => id == mergedId ? survivorId : id).Distinct().ToList();
        FollowUps = FollowUps.Select(f => f.PersonId == mergedId ? f with { PersonId = survivorId } : f).ToList();
        Growth = Growth.Select(g => g.PersonId == mergedId ? g with { PersonId = survivorId } : g).Distinct().ToList();
    }

    private void Apply(ReportContent content, IReadOnlyCollection<Guid> members, DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        if (content.MeetingDate > today.AddDays(1))
        {
            throw new DomainRuleException("groups.report_future", "A report is for a meeting that has already happened.");
        }

        if (content.AttendeeIds.Count > MaxItems || content.Visitors.Count > MaxItems || content.FollowUps.Count > MaxItems || content.Growth.Count > MaxItems)
        {
            throw new DomainRuleException("groups.report_too_long", "That's more people than a cell report can hold.");
        }

        var memberSet = members.ToHashSet();
        if (content.AttendeeIds.Any(id => !memberSet.Contains(id)) || content.Growth.Any(g => !memberSet.Contains(g.PersonId)))
        {
            throw new DomainRuleException("groups.not_a_member", "Only the cell's members can be marked present or ready for a next step.");
        }

        MeetingDate = content.MeetingDate;
        Topic = Text(content.Topic, 200, "topic");
        MaterialId = content.MaterialId;
        Notes = Text(content.Notes, MaxTextLength, "notes");
        Highlights = Text(content.Highlights, MaxTextLength, "highlights");
        PrayerNeeds = Text(content.PrayerNeeds, MaxTextLength, "prayer needs");
        Multiplication = content.Multiplication;
        AttendeeIds = content.AttendeeIds.Distinct().ToList();
        Visitors = content.Visitors.Select(CleanVisitor).ToList();
        FollowUps = content.FollowUps.Select(f => CleanFollowUp(f, memberSet)).ToList();
        Growth = content.Growth.Distinct().ToList();
        MembersPresent = AttendeeIds.Count;
        VisitorCount = Visitors.Count;
        UpdatedAt = now;
    }

    private static ReportVisitor CleanVisitor(ReportVisitor visitor)
    {
        var first = Text(visitor.FirstName, 60, "visitor's first name") ?? throw new DomainRuleException("groups.visitor_name", "Give each visitor's first name.");
        if (!visitor.AgreedToBeContacted)
        {
            // Without agreement, keep only what's needed to remember them in this report.
            return new ReportVisitor(first, null, null, null, false);
        }

        var mobile = Text(visitor.Mobile, 30, "mobile");
        var email = Text(visitor.Email, 200, "email");
        if (mobile is null && email is null)
        {
            throw new DomainRuleException("groups.visitor_contact", $"Add a mobile number or email for {first} so the church can get in touch.");
        }

        return new ReportVisitor(first, Text(visitor.LastName, 60, "visitor's last name"), mobile, email, true);
    }

    private static ReportFollowUp CleanFollowUp(ReportFollowUp followUp, HashSet<Guid> members)
    {
        if (followUp.PersonId is { } id && !members.Contains(id))
        {
            throw new DomainRuleException("groups.not_a_member", "Follow-ups can be linked only to the cell's members.");
        }

        return followUp with
        {
            Id = followUp.Id == Guid.Empty ? Guid.CreateVersion7() : followUp.Id,
            Name = Text(followUp.Name, 120, "follow-up name") ?? throw new DomainRuleException("groups.follow_up_name", "Say who needs following up."),
            Note = Text(followUp.Note, 1000, "follow-up note") ?? string.Empty,
        };
    }

    private static string? Text(string? value, int max, string field)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= max
            ? trimmed
            : throw new DomainRuleException("groups.text_too_long", $"Keep the {field} under {max} characters.");
    }
}

/// <summary>
/// What a cell teaches. Either a leader's own notes for their cell, or a church lesson the pastors write for every
/// cell in a campus or the whole church (often from Sunday's sermon). Pastors see all of them; members see only
/// the ones shared with them.
/// </summary>
public sealed class CellMaterial : AggregateRoot<Guid>
{
    public const int MaxBodyLength = 20000;

    private CellMaterial()
    {
    }

    /// <summary>The leader's cell, or null for a church lesson.</summary>
    public Guid? CellId { get; private set; }

    /// <summary>The sermon a church lesson follows, if any.</summary>
    public Guid? SermonId { get; private set; }

    public bool IsChurchLesson => CellId is null;

    public string Scope { get; private set; } = null!;

    public string Title { get; private set; } = null!;

    /// <summary>Markdown.</summary>
    public string Body { get; private set; } = null!;

    /// <summary>Optional link, e.g. to a video, a reading plan or a document.</summary>
    public string? Link { get; private set; }

    public DateOnly? ForDate { get; private set; }

    public bool SharedWithMembers { get; private set; }

    public Guid WrittenByPersonId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static CellMaterial Write(Guid cellId, ScopePath scope, string title, string body, string? link, DateOnly? forDate, bool sharedWithMembers, Guid writtenBy, DateTimeOffset now)
    {
        var material = new CellMaterial { Id = Guid.CreateVersion7(), CellId = cellId, Scope = scope.Value, WrittenByPersonId = writtenBy, CreatedAt = now };
        material.Update(title, body, link, forDate, sharedWithMembers, now);
        return material;
    }

    /// <summary>A lesson for every cell within <paramref name="scope"/> (the church or a campus).</summary>
    public static CellMaterial WriteChurchLesson(ScopePath scope, string title, string body, string? link, DateOnly? forDate, bool sharedWithMembers, Guid? sermonId, Guid writtenBy, DateTimeOffset now)
    {
        var lesson = new CellMaterial { Id = Guid.CreateVersion7(), CellId = null, SermonId = sermonId, Scope = scope.Value, WrittenByPersonId = writtenBy, CreatedAt = now };
        lesson.Update(title, body, link, forDate, sharedWithMembers, now);
        return lesson;
    }

    /// <summary>True when this church lesson is meant for a cell at <paramref name="cellScope"/>.</summary>
    public bool IsFor(string cellScope) =>
        IsChurchLesson && (cellScope == Scope || cellScope.StartsWith(Scope + ".", StringComparison.Ordinal));

    public void Update(string title, string body, string? link, DateOnly? forDate, bool sharedWithMembers, DateTimeOffset now)
    {
        var t = title?.Trim() ?? string.Empty;
        if (t.Length is 0 or > 160)
        {
            throw new DomainRuleException("groups.material_title", "Give the material a title (160 characters at most).");
        }

        var b = body?.Trim() ?? string.Empty;
        if (b.Length > MaxBodyLength)
        {
            throw new DomainRuleException("groups.material_too_long", $"Keep it under {MaxBodyLength:N0} characters.");
        }

        var l = string.IsNullOrWhiteSpace(link) ? null : link.Trim();
        if (l is not null && (l.Length > 500 || !Uri.TryCreate(l, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")))
        {
            throw new DomainRuleException("groups.material_link", "The link must be a web address starting with https://.");
        }

        Title = t;
        Body = b;
        Link = l;
        ForDate = forDate;
        SharedWithMembers = sharedWithMembers;
        UpdatedAt = now;
    }
}
