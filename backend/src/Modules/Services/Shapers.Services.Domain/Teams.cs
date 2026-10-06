namespace Shapers.Services.Domain;

/// <summary>
/// A serving team, e.g. Worship, Production, Hospitality. Its scope (a campus or a ministry) decides who may schedule it.
/// Teams not open to minors refuse under-18s; on teams that are, a minor never serves without an adult from the team.
/// </summary>
public sealed class Team : AggregateRoot<Guid>
{
    private Team()
    {
    }

    public string Name { get; private set; } = null!;

    public string Scope { get; private set; } = null!;

    public string? Description { get; private set; }

    public bool OpenToMinors { get; private set; }

    public bool IsArchived { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Team Create(string name, ScopePath scope, string? description, bool openToMinors, DateTimeOffset now)
    {
        var team = new Team { Id = Guid.CreateVersion7(), Scope = scope.Value, CreatedAt = now };
        team.Update(name, description, openToMinors);
        return team;
    }

    public void Update(string name, string? description, bool openToMinors)
    {
        Name = Text.Required(name, 80, "Give the team a name.");
        Description = Text.Optional(description, 500);
        OpenToMinors = openToMinors;
    }

    public void Archive() => IsArchived = true;

    public void Restore() => IsArchived = false;
}

/// <summary>A role on a team that people are scheduled to, e.g. Keys, Sound desk, Welcome.</summary>
public sealed class TeamPosition : Entity<Guid>
{
    private TeamPosition()
    {
    }

    public Guid TeamId { get; private set; }

    public string Name { get; private set; } = null!;

    public int Order { get; private set; }

    public bool IsArchived { get; private set; }

    public static TeamPosition Create(Guid teamId, string name, int order) =>
        new() { Id = Guid.CreateVersion7(), TeamId = teamId, Name = Text.Required(name, 60, "Give the position a name."), Order = order };

    public void Rename(string name, int order)
    {
        Name = Text.Required(name, 60, "Give the position a name.");
        Order = order;
    }

    public void Archive() => IsArchived = true;
}

/// <summary>Someone on a team, the positions they can fill, and whether they lead it.</summary>
public sealed class TeamMember : Entity<Guid>
{
    private TeamMember()
    {
    }

    public Guid TeamId { get; private set; }

    public Guid PersonId { get; private set; }

    public List<Guid> PositionIds { get; private set; } = [];

    public bool IsLeader { get; private set; }

    public DateTimeOffset AddedAt { get; private set; }

    public static TeamMember Add(Guid teamId, Guid personId, IEnumerable<Guid> positionIds, bool isLeader, DateTimeOffset now) =>
        new() { Id = Guid.CreateVersion7(), TeamId = teamId, PersonId = personId, PositionIds = positionIds.Distinct().ToList(), IsLeader = isLeader, AddedAt = now };

    public void Update(IEnumerable<Guid> positionIds, bool isLeader)
    {
        PositionIds = positionIds.Distinct().ToList();
        IsLeader = isLeader;
    }

    public void ReplacePerson(Guid survivorId) => PersonId = survivorId;
}

/// <summary>Days someone can't serve (holiday, work, exams). They aren't scheduled then.</summary>
public sealed class Blockout : Entity<Guid>
{
    private Blockout()
    {
    }

    public Guid PersonId { get; private set; }

    public DateOnly From { get; private set; }

    public DateOnly To { get; private set; }

    public string? Reason { get; private set; }

    public static Blockout Create(Guid personId, DateOnly from, DateOnly to, string? reason, DateOnly today)
    {
        if (to < from)
        {
            throw new DomainRuleException("services.blockout_order", "The last day can't be before the first day.");
        }

        if (to < today)
        {
            throw new DomainRuleException("services.blockout_past", "Choose dates from today onwards.");
        }

        if (to.DayNumber - from.DayNumber > 366)
        {
            throw new DomainRuleException("services.blockout_long", "Keep it to a year at most.");
        }

        return new Blockout { Id = Guid.CreateVersion7(), PersonId = personId, From = from, To = to, Reason = Text.Optional(reason, 200) };
    }

    public bool Covers(DateOnly date) => date >= From && date <= To;

    public void ReplacePerson(Guid survivorId) => PersonId = survivorId;
}

public enum AssignmentStatus
{
    /// <summary>Asked, no answer yet.</summary>
    Pending,
    Accepted,
    Declined,
}

public sealed record ServingRequested(Guid AssignmentId, Guid PersonId, string PlanTitle, DateOnly Date, string Team, string Position) : IDomainEvent;

public sealed record ServingDeclined(Guid AssignmentId, Guid PlanId, string PlanTitle, DateOnly Date, string Team, string Position, string Scope) : IDomainEvent;

public sealed record ServingReminderDue(Guid AssignmentId, Guid PersonId, string PlanTitle, DateOnly Date, string Team, string Position) : IDomainEvent;

/// <summary>Someone scheduled to a position on a plan, and their answer.</summary>
public sealed class Assignment : AggregateRoot<Guid>
{
    private Assignment()
    {
    }

    public Guid PlanId { get; private set; }

    public Guid TeamId { get; private set; }

    public Guid PositionId { get; private set; }

    public Guid PersonId { get; private set; }

    /// <summary>The plan's date, kept here for reminders and "my schedule".</summary>
    public DateOnly Date { get; private set; }

    public string Scope { get; private set; } = null!;

    public AssignmentStatus Status { get; private set; }

    public string? DeclineReason { get; private set; }

    public Guid RequestedByUserId { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    public DateTimeOffset? RespondedAt { get; private set; }

    public DateTimeOffset? RemindedAt { get; private set; }

    public static Assignment Request(Plan plan, Team team, TeamPosition position, Guid personId, Guid requestedBy, DateTimeOffset now)
    {
        var assignment = new Assignment
        {
            Id = Guid.CreateVersion7(),
            PlanId = plan.Id,
            TeamId = team.Id,
            PositionId = position.Id,
            PersonId = personId,
            Date = plan.Date,
            Scope = team.Scope,
            Status = AssignmentStatus.Pending,
            RequestedByUserId = requestedBy,
            RequestedAt = now,
        };
        assignment.Raise(new ServingRequested(assignment.Id, personId, plan.Title, plan.Date, team.Name, position.Name));
        return assignment;
    }

    public void Accept(DateTimeOffset now)
    {
        Status = AssignmentStatus.Accepted;
        DeclineReason = null;
        RespondedAt = now;
    }

    public void Decline(string? reason, string planTitle, string team, string position, DateTimeOffset now)
    {
        if (Status == AssignmentStatus.Declined)
        {
            return;
        }

        Status = AssignmentStatus.Declined;
        DeclineReason = Text.Optional(reason, 300);
        RespondedAt = now;
        Raise(new ServingDeclined(Id, PlanId, planTitle, Date, team, position, Scope));
    }

    /// <summary>Called a few days before: only people who said yes and haven't been reminded yet.</summary>
    public bool Remind(string planTitle, string team, string position, DateTimeOffset now)
    {
        if (Status != AssignmentStatus.Accepted || RemindedAt is not null)
        {
            return false;
        }

        RemindedAt = now;
        Raise(new ServingReminderDue(Id, PersonId, planTitle, Date, team, position));
        return true;
    }

    public void MoveTo(DateOnly date) => Date = date;

    public void ReplacePerson(Guid survivorId) => PersonId = survivorId;
}

internal static class Text
{
    public static string Required(string? value, int max, string message)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw new DomainRuleException("services.required", message);
        }

        return trimmed.Length <= max ? trimmed : throw new DomainRuleException("services.too_long", $"Keep it under {max} characters.");
    }

    public static string? Optional(string? value, int max)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= max ? trimmed : throw new DomainRuleException("services.too_long", $"Keep it under {max} characters.");
    }
}
