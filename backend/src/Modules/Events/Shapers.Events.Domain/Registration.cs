using System.Security.Cryptography;

namespace Shapers.Events.Domain;

public enum RegistrationStatus
{
    Confirmed,
    Waitlisted,
    Cancelled,
}

public enum RegistrationSource
{
    App,
    Web,
    Admin,
}

public enum CheckInOutcome
{
    CheckedIn,
    AlreadyCheckedIn,
    NotConfirmed,
}

public sealed record RegistrationCreated(Guid RegistrationId, Guid EventId, Guid PersonId, RegistrationStatus Status) : IDomainEvent;

public sealed record WaitlistPromoted(Guid RegistrationId, Guid EventId, Guid PersonId) : IDomainEvent;

public sealed record RegistrationCancelled(Guid RegistrationId, Guid EventId, Guid PersonId) : IDomainEvent;

/// <summary>A short, unguessable code printed as a QR code. Avoids look-alike characters (0/O, 1/I).</summary>
public static class TicketCode
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    public const int Length = 10;

    public static string New() => string.Create(Length, 0, static (span, _) =>
    {
        for (var i = 0; i < span.Length; i++)
        {
            span[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }
    });

    public static string Normalise(string input) => input.Trim().ToUpperInvariant().Replace("SHAPERS-T:", string.Empty, StringComparison.Ordinal);
}

public sealed class Attendee
{
    private Attendee()
    {
    }

    internal Attendee(Guid? personId, string name)
    {
        Id = Guid.CreateVersion7();
        PersonId = personId;
        Name = name;
        TicketCode = Domain.TicketCode.New();
    }

    public Guid Id { get; private set; }

    /// <summary>Set for the registrant and household members; friends named by a guest may have no record.</summary>
    public Guid? PersonId { get; private set; }

    public string Name { get; private set; } = null!;

    public string TicketCode { get; private set; } = null!;

    public DateTimeOffset? CheckedInAt { get; private set; }

    public Guid? CheckedInByUserId { get; private set; }

    internal void Anonymise()
    {
        PersonId = null;
        Name = "Removed at their request";
    }

    internal void CheckIn(Guid? by, DateTimeOffset now)
    {
        CheckedInAt = now;
        CheckedInByUserId = by;
    }
}

/// <summary>One booking: the registrant plus anyone they bring. Each attendee has their own ticket.</summary>
public sealed class Registration : AggregateRoot<Guid>
{
    private readonly List<Attendee> _attendees = [];

    private Registration()
    {
    }

    public Guid EventId { get; private set; }

    public Guid RegistrantPersonId { get; private set; }

    public RegistrationStatus Status { get; private set; }

    public RegistrationSource Source { get; private set; }

    public Dictionary<Guid, string> Answers { get; private set; } = [];

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ConfirmedAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public DateTimeOffset? ReminderSentAt { get; private set; }

    /// <summary>Keyed hash of the secret in a guest's "manage my booking" link. Null for members.</summary>
    public string? GuestKeyHash { get; private set; }

    public IReadOnlyList<Attendee> Attendees => _attendees;

    public int Seats => _attendees.Count;

    public static Registration Create(
        Event e,
        Guid registrantPersonId,
        IReadOnlyList<(Guid? PersonId, string Name)> attendees,
        IReadOnlyDictionary<Guid, string> validatedAnswers,
        RegistrationStatus status,
        RegistrationSource source,
        DateTimeOffset now,
        string? guestKeyHash = null)
    {
        if (attendees.Count == 0 || attendees.Count > e.MaxPerRegistration)
        {
            throw new DomainRuleException("events.party_size", e.MaxPerRegistration == 1
                ? "This event takes one person per registration."
                : $"You can register 1 to {e.MaxPerRegistration} people at a time.");
        }

        if (attendees.Any(a => string.IsNullOrWhiteSpace(a.Name) || a.Name.Trim().Length > 120))
        {
            throw new DomainRuleException("events.attendee_name", "Every attendee needs a name.");
        }

        if (attendees.Where(a => a.PersonId is not null).GroupBy(a => a.PersonId).Any(g => g.Count() > 1))
        {
            throw new DomainRuleException("events.duplicate_attendee", "The same person is listed twice.");
        }

        if (status == RegistrationStatus.Cancelled)
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        var registration = new Registration
        {
            Id = Guid.CreateVersion7(),
            EventId = e.Id,
            RegistrantPersonId = registrantPersonId,
            Status = status,
            Source = source,
            Answers = validatedAnswers.ToDictionary(),
            CreatedAt = now,
            ConfirmedAt = status == RegistrationStatus.Confirmed ? now : null,
            GuestKeyHash = guestKeyHash,
        };
        registration._attendees.AddRange(attendees.Select(a => new Attendee(a.PersonId, a.Name.Trim())));
        registration.Raise(new RegistrationCreated(registration.Id, e.Id, registrantPersonId, status));
        return registration;
    }

    public void Promote(DateTimeOffset now)
    {
        if (Status != RegistrationStatus.Waitlisted)
        {
            throw new DomainRuleException("events.not_waitlisted", "Only a waiting-list registration can be promoted.");
        }

        Status = RegistrationStatus.Confirmed;
        ConfirmedAt = now;
        Raise(new WaitlistPromoted(Id, EventId, RegistrantPersonId));
    }

    public void Cancel(DateTimeOffset now)
    {
        if (Status == RegistrationStatus.Cancelled)
        {
            return;
        }

        if (_attendees.Any(a => a.CheckedInAt is not null))
        {
            throw new DomainRuleException("events.already_attended", "Someone on this registration has already checked in.");
        }

        Status = RegistrationStatus.Cancelled;
        CancelledAt = now;
        Raise(new RegistrationCancelled(Id, EventId, RegistrantPersonId));
    }

    /// <summary>Checks in one attendee. Scanning twice is harmless and reports when they arrived.</summary>
    public (CheckInOutcome Outcome, Attendee Attendee) CheckIn(Guid attendeeId, Guid? by, DateTimeOffset now)
    {
        var attendee = _attendees.SingleOrDefault(a => a.Id == attendeeId)
            ?? throw new DomainRuleException("events.attendee_not_found", "That ticket isn't part of this registration.");
        if (Status != RegistrationStatus.Confirmed)
        {
            return (CheckInOutcome.NotConfirmed, attendee);
        }

        if (attendee.CheckedInAt is not null)
        {
            return (CheckInOutcome.AlreadyCheckedIn, attendee);
        }

        attendee.CheckIn(by, now);
        return (CheckInOutcome.CheckedIn, attendee);
    }

    public void MarkReminderSent(DateTimeOffset now) => ReminderSentAt = now;

    /// <summary>
    /// A person asked to be erased: their name and answers go, but the booking stays so seat counts and attendance
    /// figures remain true. Returns true when anything changed.
    /// </summary>
    public bool ErasePerson(Guid personId)
    {
        var changed = false;
        foreach (var attendee in _attendees.Where(a => a.PersonId == personId))
        {
            attendee.Anonymise();
            changed = true;
        }

        if (RegistrantPersonId == personId)
        {
            foreach (var attendee in _attendees.Where(a => a.PersonId is null))
            {
                // Guests they brought were named only by them.
                attendee.Anonymise();
            }

            RegistrantPersonId = Guid.Empty;
            Answers = [];
            GuestKeyHash = null;
            changed = true;
        }

        return changed;
    }
}

/// <summary>
/// Who gets a seat. Pure rules, so they're easy to test and reason about:
/// a new booking never jumps ahead of people already waiting, and promotion is strictly first come, first served.
/// </summary>
public static class Seating
{
    public static Result<RegistrationStatus> Decide(Event e, int seatsTaken, bool anyoneWaiting, int requested)
    {
        if (e.Capacity is not { } capacity)
        {
            return RegistrationStatus.Confirmed;
        }

        if (!anyoneWaiting && seatsTaken + requested <= capacity)
        {
            return RegistrationStatus.Confirmed;
        }

        if (requested > capacity)
        {
            return new Error("events.party_too_big", $"Only {capacity} seats exist in total.");
        }

        return e.WaitlistEnabled
            ? RegistrationStatus.Waitlisted
            : Error.Conflict("events.full", "Sorry, this event is full.");
    }

    /// <summary>
    /// The waiting-list registrations to confirm now that seats are free, in order. Stops at the first party
    /// that doesn't fit rather than skipping ahead to smaller ones.
    /// </summary>
    public static IReadOnlyList<Registration> ToPromote(Event e, int seatsTaken, IEnumerable<Registration> waitlistOldestFirst)
    {
        if (e.Capacity is not { } capacity)
        {
            return waitlistOldestFirst.ToList();
        }

        var free = capacity - seatsTaken;
        var promote = new List<Registration>();
        foreach (var registration in waitlistOldestFirst)
        {
            if (registration.Seats > free)
            {
                break;
            }

            promote.Add(registration);
            free -= registration.Seats;
        }

        return promote;
    }

    public static int SeatsLeft(Event e, int seatsTaken) => e.Capacity is { } capacity ? Math.Max(0, capacity - seatsTaken) : int.MaxValue;
}

/// <summary>A six-digit code emailed to a guest to prove the address is theirs before they register.</summary>
public sealed class EmailVerification
{
    public const int MaxAttempts = 5;
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    private EmailVerification()
    {
    }

    public Guid Id { get; private set; }

    public string Email { get; private set; } = null!;

    public string CodeHash { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset? UsedAt { get; private set; }

    public static EmailVerification Create(Guid id, string email, string codeHash, DateTimeOffset now) => new()
    {
        Id = id,
        Email = email,
        CodeHash = codeHash,
        CreatedAt = now,
        ExpiresAt = now + Lifetime,
    };

    /// <summary>True when the code matches; each verification can be used for one registration only.</summary>
    public bool TryUse(string candidateHash, DateTimeOffset now)
    {
        if (UsedAt is not null || now >= ExpiresAt || Attempts >= MaxAttempts)
        {
            return false;
        }

        Attempts++;
        if (!CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(candidateHash), System.Text.Encoding.ASCII.GetBytes(CodeHash)))
        {
            return false;
        }

        UsedAt = now;
        return true;
    }
}
