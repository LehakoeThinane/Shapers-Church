using System.Text.RegularExpressions;

namespace Shapers.Events.Domain;

public enum EventStatus
{
    Draft,
    Published,
    Cancelled,
}

/// <summary>Who can see and register: anyone (public website, WhatsApp links) or signed-in members only.</summary>
public enum EventVisibility
{
    Public,
    Members,
}

public sealed record EventLocation(string Name, string? Address);

public sealed record EventCancelled(Guid EventId, string Title) : IDomainEvent;

/// <summary>A simple extra question on the registration form, e.g. "Dietary requirements".</summary>
public sealed class RegistrationQuestion
{
    private RegistrationQuestion()
    {
    }

    internal RegistrationQuestion(Guid id, string label, bool required, int order)
    {
        Id = id;
        Label = label;
        Required = required;
        Order = order;
    }

    public Guid Id { get; private set; }

    public string Label { get; private set; } = null!;

    public bool Required { get; private set; }

    public int Order { get; private set; }
}

public sealed partial class Event : AggregateRoot<Guid>
{
    public const int MaxQuestions = 5;
    public const int MaxPerRegistrationLimit = 10;

    private readonly List<RegistrationQuestion> _questions = [];

    private Event()
    {
    }

    public string Title { get; private set; } = null!;

    /// <summary>Set once so shared links (WhatsApp, posters) keep working.</summary>
    public string Slug { get; private set; } = null!;

    public string? Summary { get; private set; }

    /// <summary>Markdown.</summary>
    public string? Description { get; private set; }

    public string Scope { get; private set; } = null!;

    public DateTimeOffset StartsAt { get; private set; }

    public DateTimeOffset EndsAt { get; private set; }

    public EventLocation? Location { get; private set; }

    public string? ImageUrl { get; private set; }

    public EventVisibility Visibility { get; private set; }

    public EventStatus Status { get; private set; }

    public bool RegistrationRequired { get; private set; }

    public DateTimeOffset? RegistrationOpensAt { get; private set; }

    /// <summary>When registration closes; defaults to the start of the event.</summary>
    public DateTimeOffset? RegistrationClosesAt { get; private set; }

    /// <summary>Seats available. Null means no limit.</summary>
    public int? Capacity { get; private set; }

    public bool WaitlistEnabled { get; private set; }

    /// <summary>How many people one registration may include (the registrant plus family or friends).</summary>
    public int MaxPerRegistration { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public IReadOnlyList<RegistrationQuestion> Questions => _questions;

    public static Event Create(string title, ScopePath scope, DateTimeOffset startsAt, DateTimeOffset endsAt, DateTimeOffset now)
    {
        var e = new Event
        {
            Id = Guid.CreateVersion7(),
            Scope = scope.Value,
            Status = EventStatus.Draft,
            Visibility = EventVisibility.Public,
            MaxPerRegistration = 1,
            CreatedAt = now,
        };
        e.UpdateDetails(title, null, null, startsAt, endsAt, null, null, EventVisibility.Public, now);
        e.Slug = $"{startsAt:yyyy-MM}-{SlugWords(e.Title)}";
        return e;
    }

    public void UseSlug(string slug) => Slug = slug;

    public void UpdateDetails(
        string title,
        string? summary,
        string? description,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        EventLocation? location,
        string? imageUrl,
        EventVisibility visibility,
        DateTimeOffset now)
    {
        EnsureNotCancelled();
        var trimmed = title?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > 150)
        {
            throw new DomainRuleException("events.title", "An event needs a title (150 characters at most).");
        }

        if (endsAt <= startsAt)
        {
            throw new DomainRuleException("events.dates", "An event must end after it starts.");
        }

        if (summary is { Length: > 300 } || description is { Length: > 20_000 })
        {
            throw new DomainRuleException("events.too_long", "The summary or description is too long.");
        }

        if (imageUrl is not null && !(Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps))
        {
            throw new DomainRuleException("events.image_url", "The image must be an https:// address.");
        }

        Title = trimmed;
        Summary = string.IsNullOrWhiteSpace(summary) ? null : summary.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        StartsAt = startsAt;
        EndsAt = endsAt;
        Location = location is null || string.IsNullOrWhiteSpace(location.Name) ? null : location with { Name = location.Name.Trim(), Address = location.Address?.Trim() };
        ImageUrl = imageUrl;
        Visibility = visibility;
        UpdatedAt = now;
    }

    public void ConfigureRegistration(
        bool required,
        DateTimeOffset? opensAt,
        DateTimeOffset? closesAt,
        int? capacity,
        bool waitlistEnabled,
        int maxPerRegistration,
        DateTimeOffset now)
    {
        EnsureNotCancelled();
        if (capacity is < 1 or > 100_000)
        {
            throw new DomainRuleException("events.capacity", "Capacity must be at least 1, or left empty for no limit.");
        }

        if (maxPerRegistration is < 1 or > MaxPerRegistrationLimit)
        {
            throw new DomainRuleException("events.max_per_registration", $"One registration can include 1 to {MaxPerRegistrationLimit} people.");
        }

        if (opensAt is { } o && closesAt is { } c && c <= o)
        {
            throw new DomainRuleException("events.registration_window", "Registration must close after it opens.");
        }

        RegistrationRequired = required;
        RegistrationOpensAt = opensAt;
        RegistrationClosesAt = closesAt;
        Capacity = capacity;
        WaitlistEnabled = waitlistEnabled && capacity is not null;
        MaxPerRegistration = maxPerRegistration;
        UpdatedAt = now;
    }

    /// <summary>Replaces the questions. Existing IDs are kept so answers already given stay linked.</summary>
    public void SetQuestions(IReadOnlyList<(Guid? Id, string Label, bool Required)> questions, DateTimeOffset now)
    {
        EnsureNotCancelled();
        if (questions.Count > MaxQuestions)
        {
            throw new DomainRuleException("events.too_many_questions", $"Ask at most {MaxQuestions} questions. Longer forms come with the Forms module.");
        }

        _questions.Clear();
        for (var i = 0; i < questions.Count; i++)
        {
            var (id, label, required) = questions[i];
            if (string.IsNullOrWhiteSpace(label) || label.Trim().Length > 200)
            {
                throw new DomainRuleException("events.question_label", "Each question needs a label (200 characters at most).");
            }

            _questions.Add(new RegistrationQuestion(id ?? Guid.CreateVersion7(), label.Trim(), required, i));
        }

        UpdatedAt = now;
    }

    public void Publish(DateTimeOffset now)
    {
        EnsureNotCancelled();
        Status = EventStatus.Published;
        PublishedAt ??= now;
        UpdatedAt = now;
    }

    public void Unpublish(DateTimeOffset now)
    {
        EnsureNotCancelled();
        Status = EventStatus.Draft;
        UpdatedAt = now;
    }

    public void Cancel(DateTimeOffset now)
    {
        if (Status == EventStatus.Cancelled)
        {
            return;
        }

        Status = EventStatus.Cancelled;
        UpdatedAt = now;
        Raise(new EventCancelled(Id, Title));
    }

    /// <summary>Why registration isn't possible right now, or null when it is.</summary>
    public string? RegistrationClosedReason(DateTimeOffset now)
    {
        if (Status != EventStatus.Published)
        {
            return Status == EventStatus.Cancelled ? "This event has been cancelled." : "This event isn't open yet.";
        }

        if (!RegistrationRequired)
        {
            return "No registration needed: just come along.";
        }

        if (RegistrationOpensAt is { } opens && now < opens)
        {
            return $"Registration opens {opens:d MMMM}.";
        }

        return now >= (RegistrationClosesAt ?? StartsAt) ? "Registration has closed." : null;
    }

    /// <summary>Checks answers against the questions: required ones answered, nothing unknown, nothing too long.</summary>
    public IReadOnlyDictionary<Guid, string> ValidateAnswers(IReadOnlyDictionary<Guid, string>? answers)
    {
        var given = answers ?? new Dictionary<Guid, string>();
        var known = _questions.ToDictionary(q => q.Id);
        if (given.Keys.Any(id => !known.ContainsKey(id)))
        {
            throw new DomainRuleException("events.unknown_question", "One of the answers doesn't match a question on this form.");
        }

        var missing = _questions.Where(q => q.Required && string.IsNullOrWhiteSpace(given.GetValueOrDefault(q.Id))).Select(q => q.Label).ToList();
        if (missing.Count > 0)
        {
            throw new DomainRuleException("events.answers_required", $"Please answer: {string.Join("; ", missing)}.");
        }

        if (given.Values.Any(v => v is { Length: > 500 }))
        {
            throw new DomainRuleException("events.answer_too_long", "Keep each answer under 500 characters.");
        }

        return given.Where(kv => !string.IsNullOrWhiteSpace(kv.Value)).ToDictionary(kv => kv.Key, kv => kv.Value.Trim());
    }

    private void EnsureNotCancelled()
    {
        if (Status == EventStatus.Cancelled)
        {
            throw new DomainRuleException("events.cancelled", "This event has been cancelled and can't be changed.");
        }
    }

    private static string SlugWords(string text)
    {
        var words = NonSlug().Replace(text.ToLowerInvariant(), "-").Trim('-');
        if (words.Length > 60)
        {
            words = words[..60].TrimEnd('-');
        }

        return words.Length == 0 ? "event" : words;
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonSlug();
}
