namespace Shapers.Communications.Domain;

/// <summary>What a notification is about. Members choose per topic and channel.</summary>
public enum Topic
{
    Live,
    Sermons,
    Events,
    Prayer,
    Announcements,

    /// <summary>Being asked to serve, reminders, and (for team leaders) when someone can't make it.</summary>
    Serving,
}

public enum Channel
{
    Push,
    Email,
}

public enum DeliveryStatus
{
    Pending,
    Sent,
    Failed,

    /// <summary>Not sent, and won't be: e.g. no device, or consent withdrawn before sending.</summary>
    Skipped,
}

/// <summary>
/// 21:00 to 07:00 in Johannesburg, nothing buzzes unless it is time-critical ("we're live").
/// Other messages wait in the queue until 07:00.
/// </summary>
public static class QuietHours
{
    private static readonly TimeZoneInfo Johannesburg = TimeZoneInfo.FindSystemTimeZoneById("Africa/Johannesburg");
    private static readonly TimeOnly Start = new(21, 0);
    private static readonly TimeOnly End = new(7, 0);

    public static DateTimeOffset DeliverAt(DateTimeOffset now, bool urgent)
    {
        if (urgent)
        {
            return now;
        }

        var local = TimeZoneInfo.ConvertTime(now, Johannesburg);
        var time = TimeOnly.FromDateTime(local.DateTime);
        if (time >= End && time < Start)
        {
            return now;
        }

        var day = DateOnly.FromDateTime(local.DateTime);
        var morning = time >= Start ? day.AddDays(1) : day;
        var at = morning.ToDateTime(End);
        return new DateTimeOffset(at, Johannesburg.GetUtcOffset(at)).ToUniversalTime();
    }
}

/// <summary>A phone that can receive push notifications for a member. One token belongs to one person at a time.</summary>
public sealed class Device
{
    private Device()
    {
    }

    public Guid Id { get; private set; }

    public Guid PersonId { get; private set; }

    /// <summary>The Expo push token, e.g. ExponentPushToken[xxxx].</summary>
    public string Token { get; private set; } = null!;

    public string Platform { get; private set; } = null!;

    public string? Name { get; private set; }

    public DateTimeOffset RegisteredAt { get; private set; }

    public DateTimeOffset LastSeenAt { get; private set; }

    public DateTimeOffset? DisabledAt { get; private set; }

    public string? DisabledReason { get; private set; }

    public bool IsActive => DisabledAt is null;

    public static Device Register(Guid personId, string token, string platform, string? name, DateTimeOffset now)
    {
        if (!IsExpoToken(token))
        {
            throw new DomainRuleException("communications.token_invalid", "That isn't a push token this app issues.");
        }

        return new Device
        {
            Id = Guid.CreateVersion7(),
            PersonId = personId,
            Token = token.Trim(),
            Platform = platform is "ios" or "android" ? platform : "unknown",
            Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim()[..Math.Min(name.Trim().Length, 100)],
            RegisteredAt = now,
            LastSeenAt = now,
        };
    }

    /// <summary>Seen again (the app registers on every start). A phone that changed hands moves to its new owner.</summary>
    public void Refresh(Guid personId, string? name, DateTimeOffset now)
    {
        PersonId = personId;
        Name = string.IsNullOrWhiteSpace(name) ? Name : name.Trim()[..Math.Min(name.Trim().Length, 100)];
        LastSeenAt = now;
        DisabledAt = null;
        DisabledReason = null;
    }

    public void Disable(string reason, DateTimeOffset now)
    {
        DisabledAt ??= now;
        DisabledReason = reason;
    }

    public static bool IsExpoToken(string? token) =>
        token is not null
        && (token.StartsWith("ExponentPushToken[", StringComparison.Ordinal) || token.StartsWith("ExpoPushToken[", StringComparison.Ordinal))
        && token.EndsWith(']')
        && token.Length <= 200;
}

/// <summary>A member's choice for one topic on one channel. Only changes from the defaults are stored.</summary>
public sealed class TopicPreference
{
    private TopicPreference()
    {
    }

    public Guid PersonId { get; private set; }

    public Topic Topic { get; private set; }

    public Channel Channel { get; private set; }

    public bool Enabled { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Push is on for everything until switched off; email only for events and announcements.</summary>
    public static bool Default(Topic topic, Channel channel) =>
        channel == Channel.Push || topic is Topic.Events or Topic.Announcements;

    public static TopicPreference Set(Guid personId, Topic topic, Channel channel, bool enabled, DateTimeOffset now) =>
        new() { PersonId = personId, Topic = topic, Channel = channel, Enabled = enabled, UpdatedAt = now };

    public void Change(bool enabled, DateTimeOffset now)
    {
        Enabled = enabled;
        UpdatedAt = now;
    }
}

/// <summary>One attempt to deliver a notification on one channel.</summary>
public sealed class Delivery
{
    public const int MaxAttempts = 3;

    private Delivery()
    {
    }

    internal Delivery(Channel channel, DateTimeOffset notBefore)
    {
        Id = Guid.CreateVersion7();
        Channel = channel;
        NotBefore = notBefore;
        Status = DeliveryStatus.Pending;
    }

    public Guid Id { get; private set; }

    public Channel Channel { get; private set; }

    public DeliveryStatus Status { get; private set; }

    public DateTimeOffset NotBefore { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    /// <summary>The provider's reference, e.g. an Expo push ticket ID.</summary>
    public string? ProviderReference { get; private set; }

    public string? Error { get; private set; }

    public void Sent(string? providerReference, DateTimeOffset now)
    {
        Attempts++;
        Status = DeliveryStatus.Sent;
        SentAt = now;
        ProviderReference = providerReference;
        Error = null;
    }

    /// <summary>A temporary failure is retried after a pause; after three tries it is given up.</summary>
    public void Failed(string error, DateTimeOffset now)
    {
        Attempts++;
        Error = error.Length > 500 ? error[..500] : error;
        if (Attempts >= MaxAttempts)
        {
            Status = DeliveryStatus.Failed;
        }
        else
        {
            NotBefore = now.AddMinutes(5 * Attempts);
        }
    }

    public void Skip(string reason)
    {
        Status = DeliveryStatus.Skipped;
        Error = reason;
    }
}

/// <summary>
/// A message to one member. It always appears in their in-app inbox; deliveries push it to their phone (and later
/// email or SMS). The source key stops the same event notifying anyone twice.
/// </summary>
public sealed class Notification : AggregateRoot<Guid>
{
    public const int MaxTitle = 120;
    public const int MaxBody = 2000;

    private readonly List<Delivery> _deliveries = [];

    private Notification()
    {
    }

    public Guid PersonId { get; private set; }

    public Topic Topic { get; private set; }

    public string Title { get; private set; } = null!;

    public string Body { get; private set; } = null!;

    /// <summary>Where tapping it goes in the app, e.g. "/sermon/faith-that-works".</summary>
    public string? Link { get; private set; }

    public string SourceKey { get; private set; } = null!;

    public bool Urgent { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ReadAt { get; private set; }

    public IReadOnlyList<Delivery> Deliveries => _deliveries;

    public static Notification Create(
        Guid personId, Topic topic, string title, string body, string? link, string sourceKey, bool urgent, IEnumerable<Channel> channels, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKey);
        if (link is not null && !link.StartsWith('/'))
        {
            throw new DomainRuleException("communications.link_invalid", "Links must be paths inside the app.");
        }

        var notification = new Notification
        {
            Id = Guid.CreateVersion7(),
            PersonId = personId,
            Topic = topic,
            Title = Truncate(title.Trim(), MaxTitle),
            Body = Truncate(body.Trim(), MaxBody),
            Link = link,
            SourceKey = sourceKey,
            Urgent = urgent,
            CreatedAt = now,
        };
        var deliverAt = QuietHours.DeliverAt(now, urgent);
        foreach (var channel in channels.Distinct())
        {
            notification._deliveries.Add(new Delivery(channel, deliverAt));
        }

        return notification;
    }

    public void MarkRead(DateTimeOffset now) => ReadAt ??= now;

    private static string Truncate(string value, int max) => value.Length <= max ? value : string.Concat(value.AsSpan(0, max - 1), "…");
}
