namespace Shapers.Privacy.Domain;

/// <summary>What a person asked for under POPIA. Access is self-service (download my data) and needs no request.</summary>
public enum DataRequestType
{
    /// <summary>Section 24: something in their record is wrong.</summary>
    Correction,

    /// <summary>Section 24: delete their personal information.</summary>
    Deletion,
}

public enum DataRequestStatus
{
    Open,
    Completed,
    Declined,
}

public sealed record PersonErasureCompleted(Guid RequestId, Guid PersonId) : IDomainEvent;

/// <summary>
/// A correction or deletion request, handled by the Information Officer. POPIA asks for a response within a
/// reasonable time; the church works to 30 days, and the queue shows anything overdue.
/// </summary>
public sealed class DataRequest : AggregateRoot<Guid>
{
    public const int MaxDetails = 2000;

    public static readonly TimeSpan ResponseTime = TimeSpan.FromDays(30);

    private DataRequest()
    {
    }

    public Guid PersonId { get; private set; }

    public DataRequestType Type { get; private set; }

    /// <summary>What to correct, or why they want deletion (optional for deletion).</summary>
    public string? Details { get; private set; }

    public DataRequestStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset DueAt { get; private set; }

    public Guid? DecidedByUserId { get; private set; }

    public DateTimeOffset? DecidedAt { get; private set; }

    /// <summary>What we told the person: what was corrected, or why we couldn't do it.</summary>
    public string? Response { get; private set; }

    public bool IsOverdue(DateTimeOffset now) => Status == DataRequestStatus.Open && now > DueAt;

    public static DataRequest Submit(Guid personId, DataRequestType type, string? details, DateTimeOffset now)
    {
        var text = details?.Trim();
        if (type == DataRequestType.Correction && string.IsNullOrEmpty(text))
        {
            throw new DomainRuleException("privacy.details_required", "Tell us what needs correcting.");
        }

        if (text?.Length > MaxDetails)
        {
            throw new DomainRuleException("privacy.too_long", $"Keep it under {MaxDetails} characters.");
        }

        return new DataRequest
        {
            Id = Guid.CreateVersion7(),
            PersonId = personId,
            Type = type,
            Details = string.IsNullOrEmpty(text) ? null : text,
            Status = DataRequestStatus.Open,
            CreatedAt = now,
            DueAt = now + ResponseTime,
        };
    }

    public void Complete(Guid byUserId, string? response, DateTimeOffset now)
    {
        EnsureOpen();
        Status = DataRequestStatus.Completed;
        Decide(byUserId, response, now);
        if (Type == DataRequestType.Deletion)
        {
            Raise(new PersonErasureCompleted(Id, PersonId));
        }
    }

    /// <summary>For example, records the law requires us to keep. The person must be told why.</summary>
    public void Decline(Guid byUserId, string reason, DateTimeOffset now)
    {
        EnsureOpen();
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainRuleException("privacy.reason_required", "Explain why, so we can tell the person.");
        }

        Status = DataRequestStatus.Declined;
        Decide(byUserId, reason, now);
    }

    private void Decide(Guid byUserId, string? response, DateTimeOffset now)
    {
        DecidedByUserId = byUserId;
        DecidedAt = now;
        Response = string.IsNullOrWhiteSpace(response) ? null : response.Trim()[..Math.Min(response.Trim().Length, MaxDetails)];
    }

    private void EnsureOpen()
    {
        if (Status != DataRequestStatus.Open)
        {
            throw new DomainRuleException("privacy.not_open", "This request has already been dealt with.");
        }
    }
}
