using Microsoft.EntityFrameworkCore;
using Shapers.People.Contracts;
using Shapers.Platform.Auditing;
using Shapers.Platform.Authorization;
using Shapers.Services.Contracts;
using Shapers.Services.Domain;

namespace Shapers.Services.Application;

/// <summary>
/// Putting people on plans. Schedulers see who's free (not away, not already serving that day) and the system keeps
/// the safeguarding rules: under-18s only on teams open to minors, and never without an adult from the same team.
/// </summary>
public sealed class ScheduleService(IServicesDb db, PlanReader reader, IPeopleDirectory people, IAuthorizer authorizer, ICurrentUser currentUser, IAuditLog audit, TimeProvider clock)
{
    private static readonly Error PlanNotFound = Error.NotFound("services.plan_not_found", "Plan not found.");

    /// <summary>People on the position's team who can fill it, with whether they're free on the plan's date.</summary>
    public async Task<Result<IReadOnlyList<CandidateDto>>> CandidatesAsync(Guid planId, Guid positionId, CancellationToken cancellationToken)
    {
        var plan = await db.Plans.AsNoTracking().SingleOrDefaultAsync(p => p.Id == planId, cancellationToken);
        var position = await db.Positions.AsNoTracking().SingleOrDefaultAsync(p => p.Id == positionId, cancellationToken);
        var team = position is null ? null : await db.Teams.AsNoTracking().SingleOrDefaultAsync(t => t.Id == position.TeamId, cancellationToken);
        if (plan is null || team is null || !await authorizer.CanAsync(ServicesPermissions.Schedule, ScopePath.Parse(team.Scope), cancellationToken))
        {
            return PlanNotFound;
        }

        var members = (await db.Members.AsNoTracking().Where(m => m.TeamId == team.Id).ToListAsync(cancellationToken))
            .Where(m => m.PositionIds.Contains(positionId))
            .ToList();
        var personIds = members.Select(m => m.PersonId).ToList();
        var names = await people.GetManyAsync(personIds, cancellationToken);
        var away = await db.Blockouts.AsNoTracking().Where(b => personIds.Contains(b.PersonId) && b.From <= plan.Date && b.To >= plan.Date).Select(b => b.PersonId).ToListAsync(cancellationToken);
        var sameDay = await db.Assignments.AsNoTracking()
            .Where(a => personIds.Contains(a.PersonId) && a.Date == plan.Date && a.Status != AssignmentStatus.Declined)
            .Select(a => new { a.PersonId, a.PlanId, a.PositionId })
            .ToListAsync(cancellationToken);
        var declinedHere = await db.Assignments.AsNoTracking().Where(a => a.PlanId == planId && a.Status == AssignmentStatus.Declined).Select(a => a.PersonId).ToListAsync(cancellationToken);
        var lastServed = await db.Assignments.AsNoTracking()
            .Where(a => personIds.Contains(a.PersonId) && a.Status == AssignmentStatus.Accepted && a.Date < plan.Date)
            .GroupBy(a => a.PersonId)
            .Select(g => new { PersonId = g.Key, Last = g.Max(a => a.Date) })
            .ToDictionaryAsync(x => x.PersonId, x => x.Last, cancellationToken);

        return members.Select(m =>
            {
                var reason = away.Contains(m.PersonId) ? "Away that day"
                    : sameDay.Any(s => s.PersonId == m.PersonId && s.PlanId == planId && s.PositionId == positionId) ? "Already scheduled here"
                    : sameDay.Any(s => s.PersonId == m.PersonId) ? "Already serving that day"
                    : declinedHere.Contains(m.PersonId) ? "Said no to this service"
                    : null;
                return new CandidateDto(m.PersonId, names.GetValueOrDefault(m.PersonId)?.DisplayName ?? "Unknown", reason is null, reason, lastServed.TryGetValue(m.PersonId, out var last) ? last : null);
            })
            // Free people first, and among them whoever served longest ago, so serving is shared out.
            .OrderBy(c => !c.Available).ThenBy(c => c.LastServed ?? DateOnly.MinValue).ThenBy(c => c.Name)
            .ToList();
    }

    public async Task<Result<PlanDto>> AssignAsync(Guid planId, AssignRequest request, CancellationToken cancellationToken)
    {
        var plan = await db.Plans.AsNoTracking().SingleOrDefaultAsync(p => p.Id == planId, cancellationToken);
        var position = await db.Positions.AsNoTracking().SingleOrDefaultAsync(p => p.Id == request.PositionId && !p.IsArchived, cancellationToken);
        var team = position is null ? null : await db.Teams.AsNoTracking().SingleOrDefaultAsync(t => t.Id == position.TeamId, cancellationToken);
        if (plan is null || position is null || team is null || !await authorizer.CanAsync(ServicesPermissions.Schedule, ScopePath.Parse(team.Scope), cancellationToken))
        {
            return PlanNotFound;
        }

        if (plan.Date < ServingTime.Today(clock))
        {
            return new Error("services.plan_past", "This service has already happened.");
        }

        var member = (await db.Members.AsNoTracking().Where(m => m.TeamId == team.Id && m.PersonId == request.PersonId).ToListAsync(cancellationToken)).SingleOrDefault();
        if (member is null || !member.PositionIds.Contains(position.Id))
        {
            return new Error("services.not_in_position", $"Add them to {team.Name} as {position.Name} first.");
        }

        if (await db.Assignments.AnyAsync(a => a.PlanId == planId && a.PositionId == position.Id && a.PersonId == request.PersonId, cancellationToken))
        {
            return Error.Conflict("services.already_scheduled", "They're already scheduled for this.");
        }

        if (await db.Blockouts.AnyAsync(b => b.PersonId == request.PersonId && b.From <= plan.Date && b.To >= plan.Date, cancellationToken))
        {
            return Error.Conflict("services.away", "They've said they're away that day.");
        }

        if (await people.IsMinorAsync(request.PersonId, cancellationToken))
        {
            if (!team.OpenToMinors)
            {
                return new Error("services.minor_not_allowed", $"{team.Name} isn't open to under-18s.");
            }

            var others = await db.Assignments.AsNoTracking()
                .Where(a => a.PlanId == planId && a.TeamId == team.Id && a.Status != AssignmentStatus.Declined && a.PersonId != request.PersonId)
                .Select(a => a.PersonId).Distinct().ToListAsync(cancellationToken);
            var anyAdult = false;
            foreach (var other in others)
            {
                if (!await people.IsMinorAsync(other, cancellationToken))
                {
                    anyAdult = true;
                    break;
                }
            }

            if (!anyAdult)
            {
                return new Error("services.adult_first", $"Schedule an adult on {team.Name} for this service first. Under-18s always serve with an adult from their team.");
            }
        }

        db.Assignments.Add(Assignment.Request(plan, team, position, request.PersonId, currentUser.UserId!.Value, clock.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord("services.scheduled", "service_plan", plan.Id.ToString(), ScopePath.Parse(team.Scope), new { request.PersonId, Position = position.Name }), cancellationToken);
        return await reader.DetailAsync(plan, cancellationToken);
    }

    public async Task<Result<PlanDto>> RemoveAsync(Guid assignmentId, CancellationToken cancellationToken)
    {
        var assignment = await db.Assignments.SingleOrDefaultAsync(a => a.Id == assignmentId, cancellationToken);
        if (assignment is null || !await authorizer.CanAsync(ServicesPermissions.Schedule, ScopePath.Parse(assignment.Scope), cancellationToken))
        {
            return Error.NotFound("services.assignment_not_found", "Not found.");
        }

        db.Assignments.Remove(assignment);
        await db.SaveChangesAsync(cancellationToken);
        var plan = await db.Plans.AsNoTracking().SingleAsync(p => p.Id == assignment.PlanId, cancellationToken);
        return await reader.DetailAsync(plan, cancellationToken);
    }

    /// <summary>The coming weeks side by side: every position needed or filled, and who's in it.</summary>
    public async Task<MatrixDto> MatrixAsync(DateOnly? from, int weeks, CancellationToken cancellationToken)
    {
        var start = from ?? ServingTime.Today(clock);
        var end = start.AddDays(Math.Clamp(weeks, 1, 12) * 7);
        var scopes = await ServingTime.ScopesAsync(authorizer, cancellationToken, ServicesPermissions.Schedule, ServicesPermissions.PlansEdit);
        var plans = await db.Plans.AsNoTracking().WithinScopes(p => p.Scope, scopes)
            .Where(p => p.Date >= start && p.Date < end)
            .OrderBy(p => p.Date).ThenBy(p => p.StartTime)
            .ToListAsync(cancellationToken);
        var planIds = plans.Select(p => p.Id).ToList();
        var assignments = await reader.AssignmentsAsync(planIds, cancellationToken);

        var positionIds = plans.SelectMany(p => p.Needs.Select(n => n.PositionId)).Concat(assignments.Select(a => a.PositionId)).Distinct().ToList();
        var positions = await db.Positions.AsNoTracking().Where(p => positionIds.Contains(p.Id)).ToListAsync(cancellationToken);
        var teamIds = positions.Select(p => p.TeamId).Distinct().ToList();
        var teams = await db.Teams.AsNoTracking().Where(t => teamIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name, cancellationToken);
        var rows = positions
            .Select(p => new MatrixRowDto(p.TeamId, teams.GetValueOrDefault(p.TeamId) ?? "Team", p.Id, p.Name))
            .OrderBy(r => r.Team).ThenBy(r => positions.First(p => p.Id == r.PositionId).Order).ThenBy(r => r.Position)
            .ToList();
        var cells = new List<MatrixCellDto>();
        foreach (var plan in plans)
        {
            foreach (var row in rows)
            {
                var here = assignments.Where(a => a.PlanId == plan.Id && a.PositionId == row.PositionId).ToList();
                var needed = plan.Needs.FirstOrDefault(n => n.PositionId == row.PositionId)?.Count ?? 0;
                if (needed > 0 || here.Count > 0)
                {
                    cells.Add(new MatrixCellDto(plan.Id, row.PositionId, here.Select(a => new MatrixPersonDto(a.Id, a.Name, a.Status)).ToList(), needed));
                }
            }
        }

        return new MatrixDto(await reader.SummariesAsync(plans, cancellationToken), rows, cells);
    }
}

/// <summary>
/// The live run sheet: the item happening now, how long it has been going, and what's next. Planners move it on;
/// staff and everyone serving on the plan can follow it.
/// </summary>
public sealed class LiveService(IServicesDb db, PlanReader reader, IAuthorizer authorizer, ICurrentUser currentUser, TimeProvider clock)
{
    private static readonly Error NotFound = Error.NotFound("services.plan_not_found", "Plan not found.");

    public async Task<Result<LiveDto>> GetAsync(Guid planId, CancellationToken cancellationToken)
    {
        var plan = await db.Plans.AsNoTracking().SingleOrDefaultAsync(p => p.Id == planId, cancellationToken);
        if (plan is null || !await CanFollowAsync(plan, cancellationToken))
        {
            return NotFound;
        }

        return await ToDtoAsync(plan, cancellationToken);
    }

    /// <summary>"start", "next", "previous", "goto" (with an item) or "end".</summary>
    public async Task<Result<LiveDto>> ControlAsync(Guid planId, string action, GoLiveRequest request, CancellationToken cancellationToken)
    {
        var plan = await db.Plans.SingleOrDefaultAsync(p => p.Id == planId, cancellationToken);
        if (plan is null || !await authorizer.CanAsync(ServicesPermissions.PlansEdit, ScopePath.Parse(plan.Scope), cancellationToken))
        {
            return NotFound;
        }

        var now = clock.GetUtcNow();
        switch (action)
        {
            case "start":
            case "goto":
                plan.GoLive(request.ItemId, now);
                break;
            case "next":
                plan.Step(1, now);
                break;
            case "previous":
                plan.Step(-1, now);
                break;
            case "end":
                plan.EndLive(now);
                break;
            default:
                return new Error("services.live_action", "Unknown action.");
        }

        await db.SaveChangesAsync(cancellationToken);
        return await ToDtoAsync(plan, cancellationToken);
    }

    private async Task<bool> CanFollowAsync(Plan plan, CancellationToken cancellationToken) =>
        await ServingTime.CanAnyAsync(authorizer, plan.Scope, cancellationToken, ServicesPermissions.PlansEdit, ServicesPermissions.Schedule)
        || (currentUser.PersonId is { } me && await db.Assignments.AnyAsync(a => a.PlanId == plan.Id && a.PersonId == me && a.Status != AssignmentStatus.Declined, cancellationToken));

    private async Task<LiveDto> ToDtoAsync(Plan plan, CancellationToken cancellationToken) =>
        new(plan.Id, plan.Title, plan.Date, plan.IsLive, plan.LiveItemId, plan.LiveItemStartedAt, clock.GetUtcNow(), await reader.ItemsAsync(plan, cancellationToken));
}
