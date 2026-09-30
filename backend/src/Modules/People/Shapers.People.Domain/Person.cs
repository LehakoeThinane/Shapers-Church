using System.Text.RegularExpressions;
using Shapers.SharedKernel;

namespace Shapers.People.Domain;

public enum PersonStatus
{
    Active,
    Inactive,
    Deceased,

    /// <summary>A duplicate folded into another record. Kept as a tombstone so old references still resolve.</summary>
    Merged,

    /// <summary>Erased at the person's request (or by retention): an empty shell so old references still resolve.</summary>
    Erased,
}

/// <summary>How the record first entered the system. Useful for data-quality work and dedup.</summary>
public enum PersonSource
{
    Admin,
    SelfRegistration,
    VisitorCard,
    EventRegistration,
    Giving,
    Import,
}

public enum Gender
{
    Female,
    Male,
}

public enum ContactType
{
    Email,
    Mobile,
    WhatsApp,
}

public sealed record PersonCreated(Guid PersonId, string Scope, PersonSource Source) : IDomainEvent;

public sealed record PersonMerged(Guid SurvivorId, Guid MergedId) : IDomainEvent;

public sealed record MembershipStatusChanged(Guid PersonId, Guid? FromStatusId, Guid ToStatusId, JourneyStage ToStage) : IDomainEvent;

public sealed record PersonMovedCampus(Guid PersonId, string FromScope, string ToScope) : IDomainEvent;

/// <summary>
/// A person the church knows about. Not a login: a Person may never have a User account.
/// Deliberately lean. Medical, giving and pastoral data live in their own modules with tighter rules.
/// </summary>
public sealed partial class Person : AggregateRoot<Guid>
{
    public const int AdultAge = 18;

    private readonly List<ContactPoint> _contacts = [];
    private readonly List<MembershipStatusChange> _statusHistory = [];

    private Person()
    {
    }

    /// <summary>Home campus (or organisation) scope. Controls which staff can see the record.</summary>
    public string Scope { get; private set; } = null!;

    public string FirstName { get; private set; } = null!;

    public string LastName { get; private set; } = null!;

    public string? PreferredName { get; private set; }

    public DateOnly? DateOfBirth { get; private set; }

    public Gender? Gender { get; private set; }

    public Guid MembershipStatusId { get; private set; }

    public PersonStatus Status { get; private set; }

    public Guid? MergedIntoId { get; private set; }

    public PersonSource Source { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<ContactPoint> Contacts => _contacts;

    public IReadOnlyList<MembershipStatusChange> StatusHistory => _statusHistory;

    public string DisplayName => $"{PreferredName ?? FirstName} {LastName}";

    public static Person Create(
        ScopePath scope,
        string firstName,
        string lastName,
        MembershipStatus initialStatus,
        PersonSource source,
        DateTimeOffset now,
        Guid? createdBy = null)
    {
        var person = new Person
        {
            Id = Guid.CreateVersion7(),
            Scope = scope.Value,
            Status = PersonStatus.Active,
            Source = source,
            CreatedAt = now,
            UpdatedAt = now,
        };
        person.Rename(firstName, lastName, null, now);
        person.MembershipStatusId = initialStatus.Id;
        person._statusHistory.Add(MembershipStatusChange.Create(null, initialStatus.Id, DateOnly.FromDateTime(now.UtcDateTime), createdBy, now));
        person.Raise(new PersonCreated(person.Id, person.Scope, source));
        return person;
    }

    public void Rename(string firstName, string lastName, string? preferredName, DateTimeOffset now)
    {
        EnsureEditable();
        FirstName = RequiredName(firstName, nameof(firstName));
        LastName = RequiredName(lastName, nameof(lastName));
        PreferredName = string.IsNullOrWhiteSpace(preferredName) ? null : preferredName.Trim();
        Touch(now);
    }

    public void SetDemographics(DateOnly? dateOfBirth, Gender? gender, DateTimeOffset now)
    {
        EnsureEditable();
        if (dateOfBirth is { } dob && dob > DateOnly.FromDateTime(now.UtcDateTime))
        {
            throw new DomainRuleException("people.dob_in_future", "Date of birth cannot be in the future.");
        }

        DateOfBirth = dateOfBirth;
        Gender = gender;
        Touch(now);
    }

    public bool IsMinorOn(DateOnly today) => DateOfBirth is { } dob && AgeOn(dob, today) < AdultAge;

    /// <summary>Adds a contact, or updates the existing one with the same type and value.</summary>
    public ContactPoint AddContact(ContactType type, string normalisedValue, bool isPrimary, bool isVerified, DateTimeOffset now)
    {
        EnsureEditable();
        ContactPoint.Validate(type, normalisedValue);

        var existing = _contacts.SingleOrDefault(c => c.Type == type && c.Value == normalisedValue);
        if (existing is null)
        {
            existing = new ContactPoint(type, normalisedValue);
            _contacts.Add(existing);
        }

        if (isVerified)
        {
            existing.MarkVerified();
        }

        if (isPrimary || !_contacts.Any(c => c.Type == type && c.IsPrimary))
        {
            foreach (var other in _contacts.Where(c => c.Type == type))
            {
                other.SetPrimary(ReferenceEquals(other, existing));
            }
        }

        Touch(now);
        return existing;
    }

    public void RemoveContact(Guid contactId, DateTimeOffset now)
    {
        EnsureEditable();
        var contact = _contacts.SingleOrDefault(c => c.Id == contactId)
            ?? throw new DomainRuleException("people.contact_not_found", "Contact not found.");
        _contacts.Remove(contact);
        if (contact.IsPrimary && _contacts.FirstOrDefault(c => c.Type == contact.Type) is { } next)
        {
            next.SetPrimary(true);
        }

        Touch(now);
    }

    public ContactPoint? PrimaryContact(ContactType type) =>
        _contacts.FirstOrDefault(c => c.Type == type && c.IsPrimary) ?? _contacts.FirstOrDefault(c => c.Type == type);

    public void ChangeMembershipStatus(MembershipStatus status, DateOnly effectiveDate, Guid? changedBy, DateTimeOffset now)
    {
        EnsureEditable();
        if (status.Id == MembershipStatusId)
        {
            return;
        }

        var from = MembershipStatusId;
        MembershipStatusId = status.Id;
        _statusHistory.Add(MembershipStatusChange.Create(from, status.Id, effectiveDate, changedBy, now));
        Touch(now);
        Raise(new MembershipStatusChanged(Id, from, status.Id, status.Stage));
    }

    public void MoveTo(ScopePath scope, DateTimeOffset now)
    {
        EnsureEditable();
        if (scope.Value == Scope)
        {
            return;
        }

        var from = Scope;
        Scope = scope.Value;
        Touch(now);
        Raise(new PersonMovedCampus(Id, from, Scope));
    }

    public void Deactivate(DateTimeOffset now)
    {
        EnsureEditable();
        Status = PersonStatus.Inactive;
        Touch(now);
    }

    public void Reactivate(DateTimeOffset now)
    {
        if (Status != PersonStatus.Inactive)
        {
            throw new DomainRuleException("people.not_inactive", "Only inactive people can be reactivated.");
        }

        Status = PersonStatus.Active;
        Touch(now);
    }

    public void MarkDeceased(DateTimeOffset now)
    {
        EnsureEditable();
        Status = PersonStatus.Deceased;
        Touch(now);
    }

    /// <summary>
    /// Removes everything that identifies the person. The record stays as an empty shell so bookings, audit entries
    /// and giving records that point at it still resolve, but it can't be edited or found again.
    /// </summary>
    public void Erase(DateTimeOffset now)
    {
        FirstName = "Removed";
        LastName = "at their request";
        PreferredName = null;
        DateOfBirth = null;
        Gender = null;
        _contacts.Clear();
        Status = PersonStatus.Erased;
        Touch(now);
    }

    /// <summary>Called on the duplicate. Use <see cref="PersonMerger"/> rather than calling this directly.</summary>
    internal void BecomeMergedInto(Person survivor, DateTimeOffset now)
    {
        Status = PersonStatus.Merged;
        MergedIntoId = survivor.Id;
        Touch(now);
        Raise(new PersonMerged(survivor.Id, Id));
    }

    /// <summary>Called on the survivor: takes over anything the duplicate knew that the survivor did not.</summary>
    internal void Absorb(Person duplicate, DateTimeOffset now)
    {
        foreach (var contact in duplicate.Contacts)
        {
            AddContact(contact.Type, contact.Value, isPrimary: false, contact.IsVerified, now);
        }

        DateOfBirth ??= duplicate.DateOfBirth;
        Gender ??= duplicate.Gender;
        PreferredName ??= duplicate.PreferredName;
        Touch(now);
    }

    private void EnsureEditable()
    {
        if (Status == PersonStatus.Merged)
        {
            throw new DomainRuleException("people.merged", "This record was merged into another person and can no longer change.");
        }

        if (Status == PersonStatus.Erased)
        {
            throw new DomainRuleException("people.erased", "This record was erased and can no longer change.");
        }
    }

    private void Touch(DateTimeOffset now) => UpdatedAt = now;

    private static int AgeOn(DateOnly dob, DateOnly today)
    {
        var age = today.Year - dob.Year;
        return dob > today.AddYears(-age) ? age - 1 : age;
    }

    private static string RequiredName(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainRuleException("people.name_required", $"{field} is required.");
        }

        var trimmed = MultipleSpaces().Replace(value.Trim(), " ");
        return trimmed.Length > 100
            ? throw new DomainRuleException("people.name_too_long", $"{field} must be 100 characters or fewer.")
            : trimmed;
    }

    [GeneratedRegex("\\s+")]
    private static partial Regex MultipleSpaces();
}

public sealed partial class ContactPoint
{
    private ContactPoint()
    {
    }

    internal ContactPoint(ContactType type, string value)
    {
        Id = Guid.CreateVersion7();
        Type = type;
        Value = value;
    }

    public Guid Id { get; private set; }

    public ContactType Type { get; private set; }

    /// <summary>Normalised: lower-case email, or E.164 phone number (+27...).</summary>
    public string Value { get; private set; } = null!;

    public bool IsPrimary { get; private set; }

    /// <summary>True once the person has proved they control it (e.g. by entering a one-time code).</summary>
    public bool IsVerified { get; private set; }

    internal void SetPrimary(bool value) => IsPrimary = value;

    internal void MarkVerified() => IsVerified = true;

    public static void Validate(ContactType type, string value)
    {
        var valid = type switch
        {
            ContactType.Email => value.Length <= 254 && !value.Any(char.IsUpper) && EmailShape().IsMatch(value),
            ContactType.Mobile or ContactType.WhatsApp => E164().IsMatch(value),
            _ => false,
        };
        if (!valid)
        {
            throw new DomainRuleException("people.contact_invalid", $"'{value}' is not a valid normalised {type} value.");
        }
    }

    [GeneratedRegex("^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$")]
    private static partial Regex EmailShape();

    [GeneratedRegex("^\\+[1-9][0-9]{6,14}$")]
    private static partial Regex E164();
}

public sealed class MembershipStatusChange
{
    private MembershipStatusChange()
    {
    }

    public Guid Id { get; private set; }

    public Guid? FromStatusId { get; private set; }

    public Guid ToStatusId { get; private set; }

    public DateOnly EffectiveDate { get; private set; }

    public Guid? ChangedByUserId { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    internal static MembershipStatusChange Create(Guid? from, Guid to, DateOnly effective, Guid? by, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        FromStatusId = from,
        ToStatusId = to,
        EffectiveDate = effective,
        ChangedByUserId = by,
        RecordedAt = now,
    };
}
