namespace Shapers.Privacy.Domain;

public enum BreachStatus
{
    Open,
    Closed,
}

/// <summary>
/// A security compromise involving personal information (POPIA section 22). The Regulator and the people affected
/// must be told as soon as reasonably possible after it is discovered, unless their identity can't be established.
/// </summary>
public sealed class Breach : AggregateRoot<Guid>
{
    public const int MaxText = 4000;

    /// <summary>After this long without telling the Regulator, the register flags the breach.</summary>
    public static readonly TimeSpan NotifyWithin = TimeSpan.FromHours(72);

    private Breach()
    {
    }

    public string Title { get; private set; } = null!;

    public string Description { get; private set; } = null!;

    public DateTimeOffset DiscoveredAt { get; private set; }

    public DateTimeOffset? OccurredAt { get; private set; }

    /// <summary>What kind of information was involved, e.g. "names and phone numbers of the youth ministry".</summary>
    public string? DataInvolved { get; private set; }

    public int? PeopleAffected { get; private set; }

    /// <summary>Religious, health, children's or other special personal information was involved.</summary>
    public bool SpecialInformation { get; private set; }

    /// <summary>What was done to stop it and limit the harm.</summary>
    public string? Containment { get; private set; }

    public DateTimeOffset? RegulatorNotifiedAt { get; private set; }

    public DateTimeOffset? PeopleNotifiedAt { get; private set; }

    public BreachStatus Status { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public Guid RecordedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool NotificationOverdue(DateTimeOffset now) => RegulatorNotifiedAt is null && now - DiscoveredAt > NotifyWithin;

    public static Breach Record(string title, string description, DateTimeOffset discoveredAt, Guid byUserId, DateTimeOffset now)
    {
        if (discoveredAt > now.AddMinutes(5))
        {
            throw new DomainRuleException("privacy.discovered_future", "The discovery date can't be in the future.");
        }

        var breach = new Breach { Id = Guid.CreateVersion7(), RecordedByUserId = byUserId, CreatedAt = now, Status = BreachStatus.Open, DiscoveredAt = discoveredAt };
        breach.Title = Required(title, 150, "Give it a short title.");
        breach.Description = Required(description, MaxText, "Describe what happened.");
        breach.UpdatedAt = now;
        return breach;
    }

    public void Update(
        string title,
        string description,
        DateTimeOffset? occurredAt,
        string? dataInvolved,
        int? peopleAffected,
        bool specialInformation,
        string? containment,
        DateTimeOffset? regulatorNotifiedAt,
        DateTimeOffset? peopleNotifiedAt,
        DateTimeOffset now)
    {
        Title = Required(title, 150, "Give it a short title.");
        Description = Required(description, MaxText, "Describe what happened.");
        if (peopleAffected < 0)
        {
            throw new DomainRuleException("privacy.people_affected", "The number of people affected can't be negative.");
        }

        if (regulatorNotifiedAt < DiscoveredAt.AddDays(-1) || peopleNotifiedAt < DiscoveredAt.AddDays(-1))
        {
            throw new DomainRuleException("privacy.notified_before_discovery", "Notification dates can't be before the breach was discovered.");
        }

        OccurredAt = occurredAt;
        DataInvolved = Optional(dataInvolved);
        PeopleAffected = peopleAffected;
        SpecialInformation = specialInformation;
        Containment = Optional(containment);
        RegulatorNotifiedAt = regulatorNotifiedAt;
        PeopleNotifiedAt = peopleNotifiedAt;
        UpdatedAt = now;
    }

    /// <summary>Closing needs the Regulator told and the harm contained, so nothing is closed half-done.</summary>
    public void Close(DateTimeOffset now)
    {
        if (RegulatorNotifiedAt is null)
        {
            throw new DomainRuleException("privacy.regulator_not_notified", "Record when the Information Regulator was told before closing.");
        }

        if (Containment is null)
        {
            throw new DomainRuleException("privacy.containment_missing", "Record what was done to contain it before closing.");
        }

        Status = BreachStatus.Closed;
        ClosedAt = now;
        UpdatedAt = now;
    }

    public void Reopen(DateTimeOffset now)
    {
        Status = BreachStatus.Open;
        ClosedAt = null;
        UpdatedAt = now;
    }

    private static string Required(string value, int max, string message)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw new DomainRuleException("privacy.required", message);
        }

        return trimmed.Length <= max ? trimmed : throw new DomainRuleException("privacy.too_long", $"Keep it under {max} characters.");
    }

    private static string? Optional(string? value)
    {
        var trimmed = value?.Trim();
        if (trimmed?.Length > MaxText)
        {
            throw new DomainRuleException("privacy.too_long", $"Keep it under {MaxText} characters.");
        }

        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
