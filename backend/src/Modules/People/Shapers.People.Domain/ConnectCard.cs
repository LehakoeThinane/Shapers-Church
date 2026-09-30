namespace Shapers.People.Domain;

/// <summary>What someone asked for on a connect card. More than one can apply.</summary>
public enum ConnectReason
{
    FirstTime,
    Decision,
    Prayer,
    MoreInfo,
    JoinGroup,
    Serve,
}

public enum ConnectCardStatus
{
    New,
    Handled,
}

public sealed record ConnectCardSubmitted(Guid CardId, Guid PersonId, string Scope, IReadOnlyList<ConnectReason> Reasons, string? PrayerText) : IDomainEvent;

/// <summary>
/// "I'm new", "I made a decision", "please pray for me": a hand raised during a service or on the website.
/// Staff follow up and mark it handled. Prayer requests are also filed with the pastors in the Prayer module.
/// </summary>
public sealed class ConnectCard : AggregateRoot<Guid>
{
    private ConnectCard()
    {
    }

    public Guid PersonId { get; private set; }

    /// <summary>The person's scope when they submitted, so the right campus team sees it.</summary>
    public string Scope { get; private set; } = null!;

    public List<ConnectReason> Reasons { get; private set; } = [];

    public string? Message { get; private set; }

    /// <summary>Where it came from, e.g. "livestream" or "website".</summary>
    public string Source { get; private set; } = null!;

    public Guid? SourceId { get; private set; }

    public ConnectCardStatus Status { get; private set; }

    public DateTimeOffset SubmittedAt { get; private set; }

    public Guid? HandledByUserId { get; private set; }

    public DateTimeOffset? HandledAt { get; private set; }

    public string? HandlerNote { get; private set; }

    public static ConnectCard Submit(Guid personId, ScopePath scope, IEnumerable<ConnectReason> reasons, string? message, string source, Guid? sourceId, DateTimeOffset now)
    {
        var distinct = reasons.Distinct().ToList();
        if (distinct.Count == 0)
        {
            throw new DomainRuleException("people.connect_reason", "Choose at least one reason.");
        }

        var trimmed = string.IsNullOrWhiteSpace(message) ? null : message.Trim();
        if (trimmed is { Length: > 2000 })
        {
            throw new DomainRuleException("people.connect_message", "Keep the message under 2,000 characters.");
        }

        var card = new ConnectCard
        {
            Id = Guid.CreateVersion7(),
            PersonId = personId,
            Scope = scope.Value,
            Reasons = distinct,
            Message = trimmed,
            Source = string.IsNullOrWhiteSpace(source) ? "app" : source.Trim().ToLowerInvariant()[..Math.Min(source.Trim().Length, 30)],
            SourceId = sourceId,
            Status = ConnectCardStatus.New,
            SubmittedAt = now,
        };
        card.Raise(new ConnectCardSubmitted(card.Id, personId, card.Scope, distinct, distinct.Contains(ConnectReason.Prayer) ? card.Message : null));
        return card;
    }

    public void MarkHandled(Guid? by, string? note, DateTimeOffset now)
    {
        Status = ConnectCardStatus.Handled;
        HandledByUserId = by;
        HandledAt = now;
        HandlerNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim()[..Math.Min(note.Trim().Length, 1000)];
    }
}
