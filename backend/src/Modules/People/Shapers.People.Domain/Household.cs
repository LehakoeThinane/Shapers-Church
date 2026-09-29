using Shapers.SharedKernel;

namespace Shapers.People.Domain;

public enum HouseholdRole
{
    Adult,
    Child,
}

/// <summary>
/// People who live together. A person can belong to several households; separated parents are the
/// common case, and Kids check-in relies on it to know who may collect a child.
/// </summary>
public sealed class Household : AggregateRoot<Guid>
{
    private readonly List<HouseholdMember> _members = [];

    private Household()
    {
    }

    public string Name { get; private set; } = null!;

    public string Scope { get; private set; } = null!;

    public Guid? PrimaryContactId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyList<HouseholdMember> Members => _members;

    public static Household Create(string name, ScopePath scope, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new Household
        {
            Id = Guid.CreateVersion7(),
            Name = name.Trim(),
            Scope = scope.Value,
            CreatedAt = now,
        };
    }

    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
    }

    public void AddMember(Guid personId, HouseholdRole role)
    {
        if (_members.Any(m => m.PersonId == personId))
        {
            throw new DomainRuleException("people.already_in_household", "This person is already in the household.");
        }

        _members.Add(new HouseholdMember(personId, role));
        if (PrimaryContactId is null && role == HouseholdRole.Adult)
        {
            PrimaryContactId = personId;
        }
    }

    public void RemoveMember(Guid personId)
    {
        var member = _members.SingleOrDefault(m => m.PersonId == personId)
            ?? throw new DomainRuleException("people.not_in_household", "This person is not in the household.");
        _members.Remove(member);
        if (PrimaryContactId == personId)
        {
            PrimaryContactId = _members.FirstOrDefault(m => m.Role == HouseholdRole.Adult)?.PersonId;
        }
    }

    public void SetPrimaryContact(Guid personId)
    {
        var member = _members.SingleOrDefault(m => m.PersonId == personId)
            ?? throw new DomainRuleException("people.not_in_household", "This person is not in the household.");
        if (member.Role != HouseholdRole.Adult)
        {
            throw new DomainRuleException("people.primary_must_be_adult", "The primary contact must be an adult in the household.");
        }

        PrimaryContactId = personId;
    }

    /// <summary>Re-points a merged duplicate to the surviving record.</summary>
    internal void ReplacePerson(Guid mergedId, Guid survivorId)
    {
        var merged = _members.SingleOrDefault(m => m.PersonId == mergedId);
        if (merged is null)
        {
            return;
        }

        _members.Remove(merged);
        if (_members.All(m => m.PersonId != survivorId))
        {
            _members.Add(new HouseholdMember(survivorId, merged.Role));
        }

        if (PrimaryContactId == mergedId)
        {
            PrimaryContactId = survivorId;
        }
    }
}

public sealed class HouseholdMember
{
    private HouseholdMember()
    {
    }

    internal HouseholdMember(Guid personId, HouseholdRole role)
    {
        PersonId = personId;
        Role = role;
    }

    public Guid PersonId { get; private set; }

    public HouseholdRole Role { get; private set; }
}
