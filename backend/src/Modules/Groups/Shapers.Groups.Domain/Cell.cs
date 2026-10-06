namespace Shapers.Groups.Domain;

public enum CellStatus
{
    Active,
    Closed,
}

public enum CellRole
{
    Leader,
    CoLeader,
    Member,
}

/// <summary>
/// A home cell: a small group meeting in someone's home. Its leaders (leader and co-leaders) look after the members,
/// write a report after each meeting and prepare what they teach. Pastors see every cell at their campus.
/// </summary>
public sealed class Cell : AggregateRoot<Guid>
{
    public const int MaxNameLength = 80;

    private readonly List<CellMember> _members = [];

    private Cell()
    {
    }

    public string Name { get; private set; } = null!;

    public Guid CampusId { get; private set; }

    /// <summary>The campus scope, so that campus's pastors see the cell, its reports and its materials.</summary>
    public string Scope { get; private set; } = null!;

    public DayOfWeek? MeetingDay { get; private set; }

    public TimeOnly? MeetingTime { get; private set; }

    /// <summary>A general area (e.g. "Rivonia"), safe to show to anyone looking for a cell.</summary>
    public string? Area { get; private set; }

    /// <summary>The host's address. Only the cell's members, its leaders and pastors see it.</summary>
    public string? Address { get; private set; }

    public CellStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public IReadOnlyList<CellMember> Members => _members;

    public IEnumerable<CellMember> ActiveMembers => _members.Where(m => m.LeftAt is null);

    public static Cell Create(string name, Guid campusId, ScopePath campusScope, DayOfWeek? meetingDay, TimeOnly? meetingTime, string? area, string? address, DateTimeOffset now)
    {
        var cell = new Cell
        {
            Id = Guid.CreateVersion7(),
            CampusId = campusId,
            Scope = campusScope.Value,
            Status = CellStatus.Active,
            CreatedAt = now,
        };
        cell.SetDetails(name, meetingDay, meetingTime, area, address);
        return cell;
    }

    public void UpdateDetails(string name, DayOfWeek? meetingDay, TimeOnly? meetingTime, string? area, string? address)
    {
        EnsureActive();
        SetDetails(name, meetingDay, meetingTime, area, address);
    }

    /// <summary>Adds someone, or changes their role if they already belong to the cell.</summary>
    public CellMember AddMember(Guid personId, CellRole role, DateTimeOffset now)
    {
        EnsureActive();
        var current = _members.SingleOrDefault(m => m.PersonId == personId && m.LeftAt is null);
        if (current is not null)
        {
            current.ChangeRole(role);
            return current;
        }

        var member = CellMember.Join(Id, personId, role, now);
        _members.Add(member);
        return member;
    }

    public void RemoveMember(Guid personId, DateTimeOffset now)
    {
        var current = _members.SingleOrDefault(m => m.PersonId == personId && m.LeftAt is null)
            ?? throw new DomainRuleException("groups.not_a_member", "That person isn't in this cell.");
        current.Leave(now);
    }

    /// <summary>Leaders and co-leaders write reports and materials for the cell.</summary>
    public bool IsLedBy(Guid personId) => ActiveMembers.Any(m => m.PersonId == personId && m.Role is CellRole.Leader or CellRole.CoLeader);

    public bool HasMember(Guid personId) => ActiveMembers.Any(m => m.PersonId == personId);

    public void Close(DateTimeOffset now)
    {
        if (Status == CellStatus.Closed)
        {
            return;
        }

        Status = CellStatus.Closed;
        ClosedAt = now;
    }

    /// <summary>Points records of a merged person at the surviving one.</summary>
    public void ReplacePerson(Guid mergedId, Guid survivorId, DateTimeOffset now)
    {
        foreach (var member in _members.Where(m => m.PersonId == mergedId && m.LeftAt is null).ToList())
        {
            if (HasMember(survivorId))
            {
                member.Leave(now);
            }
            else
            {
                member.ReplacePerson(survivorId);
            }
        }
    }

    private void SetDetails(string name, DayOfWeek? meetingDay, TimeOnly? meetingTime, string? area, string? address)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > MaxNameLength)
        {
            throw new DomainRuleException("groups.name_invalid", $"Give the cell a name ({MaxNameLength} characters at most).");
        }

        Name = trimmed;
        MeetingDay = meetingDay;
        MeetingTime = meetingTime;
        Area = Optional(area, 100, "area");
        Address = Optional(address, 300, "address");
    }

    private void EnsureActive()
    {
        if (Status == CellStatus.Closed)
        {
            throw new DomainRuleException("groups.cell_closed", "This cell is closed.");
        }
    }

    private static string? Optional(string? value, int max, string field)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= max ? trimmed : throw new DomainRuleException($"groups.{field}_too_long", $"Keep the {field} under {max} characters.");
    }
}

public sealed class CellMember
{
    private CellMember()
    {
    }

    public Guid Id { get; private set; }

    public Guid CellId { get; private set; }

    public Guid PersonId { get; private set; }

    public CellRole Role { get; private set; }

    public DateTimeOffset JoinedAt { get; private set; }

    public DateTimeOffset? LeftAt { get; private set; }

    internal static CellMember Join(Guid cellId, Guid personId, CellRole role, DateTimeOffset now) =>
        new() { Id = Guid.CreateVersion7(), CellId = cellId, PersonId = personId, Role = role, JoinedAt = now };

    internal void ChangeRole(CellRole role) => Role = role;

    internal void Leave(DateTimeOffset now) => LeftAt ??= now;

    internal void ReplacePerson(Guid personId) => PersonId = personId;
}
