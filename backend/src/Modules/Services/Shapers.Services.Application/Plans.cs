using Microsoft.EntityFrameworkCore;
using Shapers.Church.Contracts;
using Shapers.People.Contracts;
using Shapers.Platform.Auditing;
using Shapers.Platform.Authorization;
using Shapers.Services.Contracts;
using Shapers.Services.Domain;

namespace Shapers.Services.Application;

/// <summary>
/// Service types (templates) and plans. Planners (services.plans.edit) build them; schedulers (services.schedule) can
/// read them to fill the teams.
/// </summary>
public sealed class PlanService(IServicesDb db, PlanReader reader, IChurchDirectory church, IAuthorizer authorizer, IAuditLog audit, TimeProvider clock)
{
    private static readonly Error NotFound = Error.NotFound("services.plan_not_found", "Plan not found.");
    private static readonly Error TypeNotFound = Error.NotFound("services.type_not_found", "Service type not found.");

    // ---------- Service types ----------

    public async Task<IReadOnlyList<ServiceTypeDto>> TypesAsync(CancellationToken cancellationToken)
    {
        var scopes = await ServingTime.ScopesAsync(authorizer, cancellationToken, ServicesPermissions.PlansEdit, ServicesPermissions.Schedule);
        return (await db.ServiceTypes.AsNoTracking().WithinScopes(t => t.Scope, scopes).Where(t => !t.IsArchived).OrderBy(t => t.Name).ToListAsync(cancellationToken))
            .Select(ToDto).ToList();
    }

    public async Task<Result<ServiceTypeDto>> CreateTypeAsync(SaveServiceTypeRequest request, CancellationToken cancellationToken)
    {
        var scope = await ServingTime.ChooseScopeAsync(authorizer, church, ServicesPermissions.PlansEdit, request.Scope, cancellationToken);
        if (scope.IsFailure)
        {
            return scope.Error!;
        }

        var check = await CheckContentAsync(request.Items, request.Needs, cancellationToken);
        if (check.IsFailure)
        {
            return check.Error!;
        }

        var type = ServiceType.Create(request.Name, scope.Value, request.StartTime, request.Items, request.Needs);
        db.ServiceTypes.Add(type);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(type);
    }

    public async Task<Result<ServiceTypeDto>> UpdateTypeAsync(Guid id, SaveServiceTypeRequest request, CancellationToken cancellationToken)
    {
        var type = await db.ServiceTypes.SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (type is null || !await authorizer.CanAsync(ServicesPermissions.PlansEdit, ScopePath.Parse(type.Scope), cancellationToken))
        {
            return TypeNotFound;
        }

        var check = await CheckContentAsync(request.Items, request.Needs, cancellationToken);
        if (check.IsFailure)
        {
            return check.Error!;
        }

        type.Update(request.Name, request.StartTime, request.Items, request.Needs);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(type);
    }

    public async Task<Result> ArchiveTypeAsync(Guid id, CancellationToken cancellationToken)
    {
        var type = await db.ServiceTypes.SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (type is null || !await authorizer.CanAsync(ServicesPermissions.PlansEdit, ScopePath.Parse(type.Scope), cancellationToken))
        {
            return TypeNotFound;
        }

        type.Archive();
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    // ---------- Plans ----------

    public async Task<IReadOnlyList<PlanSummaryDto>> ListAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        var start = from ?? ServingTime.Today(clock).AddDays(-7);
        var end = to ?? start.AddDays(120);
        var scopes = await ServingTime.ScopesAsync(authorizer, cancellationToken, ServicesPermissions.PlansEdit, ServicesPermissions.Schedule);
        var plans = await db.Plans.AsNoTracking().WithinScopes(p => p.Scope, scopes)
            .Where(p => p.Date >= start && p.Date <= end)
            .OrderBy(p => p.Date).ThenBy(p => p.StartTime)
            .ToListAsync(cancellationToken);
        return await reader.SummariesAsync(plans, cancellationToken);
    }

    public async Task<Result<PlanDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var plan = await db.Plans.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        return plan is null || !await ServingTime.CanAnyAsync(authorizer, plan.Scope, cancellationToken, ServicesPermissions.PlansEdit, ServicesPermissions.Schedule)
            ? NotFound
            : await reader.DetailAsync(plan, cancellationToken);
    }

    public async Task<Result<PlanDto>> CreateAsync(CreatePlanRequest request, CancellationToken cancellationToken)
    {
        Plan plan;
        if (request.ServiceTypeId is { } typeId)
        {
            var type = await db.ServiceTypes.AsNoTracking().SingleOrDefaultAsync(t => t.Id == typeId, cancellationToken);
            if (type is null || !await authorizer.CanAsync(ServicesPermissions.PlansEdit, ScopePath.Parse(type.Scope), cancellationToken))
            {
                return TypeNotFound;
            }

            plan = Plan.FromType(type, request.Date, request.Title, clock.GetUtcNow());
            if (request.StartTime is { } start)
            {
                plan.UpdateDetails(plan.Title, plan.Date, start, null, null, null, clock.GetUtcNow());
            }
        }
        else
        {
            var scope = await ServingTime.ChooseScopeAsync(authorizer, church, ServicesPermissions.PlansEdit, request.Scope, cancellationToken);
            if (scope.IsFailure)
            {
                return scope.Error!;
            }

            plan = Plan.Create(request.Title ?? "Service", request.Date, request.StartTime ?? new TimeOnly(9, 0), scope.Value, clock.GetUtcNow());
        }

        db.Plans.Add(plan);
        await db.SaveChangesAsync(cancellationToken);
        await AuditAsync("services.plan.created", plan, cancellationToken);
        return await reader.DetailAsync(plan, cancellationToken);
    }

    public Task<Result<PlanDto>> UpdateDetailsAsync(Guid id, SavePlanDetailsRequest request, CancellationToken cancellationToken) =>
        ChangeAsync(id, "services.plan.updated", async (plan, ct) =>
        {
            var dateChanged = plan.Date != request.Date;
            plan.UpdateDetails(request.Title, request.Date, request.StartTime, request.SeriesTitle, request.Notes, request.LivestreamId, clock.GetUtcNow());
            if (dateChanged)
            {
                foreach (var assignment in await db.Assignments.Where(a => a.PlanId == plan.Id).ToListAsync(ct))
                {
                    assignment.MoveTo(request.Date);
                }
            }

            return Result.Success();
        }, cancellationToken);

    public Task<Result<PlanDto>> SetItemsAsync(Guid id, SavePlanItemsRequest request, CancellationToken cancellationToken) =>
        ChangeAsync(id, "services.plan.items_changed", async (plan, ct) =>
        {
            var check = await CheckContentAsync(request.Items, [], ct);
            if (check.IsFailure)
            {
                return check;
            }

            plan.SetItems(request.Items, clock.GetUtcNow());
            return Result.Success();
        }, cancellationToken);

    public Task<Result<PlanDto>> SetNeedsAsync(Guid id, SavePlanNeedsRequest request, CancellationToken cancellationToken) =>
        ChangeAsync(id, "services.plan.needs_changed", async (plan, ct) =>
        {
            var check = await CheckContentAsync([], request.Needs, ct);
            if (check.IsFailure)
            {
                return check;
            }

            plan.SetNeeds(request.Needs, clock.GetUtcNow());
            return Result.Success();
        }, cancellationToken);

    /// <summary>Removes a plan and everyone's place on it.</summary>
    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var plan = await db.Plans.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (plan is null || !await authorizer.CanAsync(ServicesPermissions.PlansEdit, ScopePath.Parse(plan.Scope), cancellationToken))
        {
            return NotFound;
        }

        await db.Assignments.Where(a => a.PlanId == id).ExecuteDeleteAsync(cancellationToken);
        db.Plans.Remove(plan);
        await db.SaveChangesAsync(cancellationToken);
        await AuditAsync("services.plan.deleted", plan, cancellationToken);
        return Result.Success();
    }

    private async Task<Result<PlanDto>> ChangeAsync(Guid id, string action, Func<Plan, CancellationToken, Task<Result>> change, CancellationToken cancellationToken)
    {
        var plan = await db.Plans.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (plan is null || !await authorizer.CanAsync(ServicesPermissions.PlansEdit, ScopePath.Parse(plan.Scope), cancellationToken))
        {
            return NotFound;
        }

        var result = await change(plan, cancellationToken);
        if (result.IsFailure)
        {
            return result.Error!;
        }

        await db.SaveChangesAsync(cancellationToken);
        await AuditAsync(action, plan, cancellationToken);
        return await reader.DetailAsync(plan, cancellationToken);
    }

    /// <summary>Songs and positions used in an order of service must exist.</summary>
    private async Task<Result> CheckContentAsync(IReadOnlyList<PlanItem> items, IReadOnlyList<PositionNeed> needs, CancellationToken cancellationToken)
    {
        var songIds = items.Where(i => i.SongId is not null).Select(i => i.SongId!.Value).Distinct().ToList();
        if (songIds.Count > 0 && await db.Songs.CountAsync(s => songIds.Contains(s.Id), cancellationToken) != songIds.Count)
        {
            return Error.NotFound("services.song_not_found", "One of the songs wasn't found.");
        }

        var positionIds = needs.Select(n => n.PositionId).Distinct().ToList();
        if (positionIds.Count > 0 && await db.Positions.CountAsync(p => positionIds.Contains(p.Id), cancellationToken) != positionIds.Count)
        {
            return Error.NotFound("services.position_not_found", "One of the positions wasn't found.");
        }

        return Result.Success();
    }

    private static ServiceTypeDto ToDto(ServiceType t) => new(t.Id, t.Name, t.StartTime, t.Scope, t.Items, t.Needs);

    private Task AuditAsync(string action, Plan plan, CancellationToken cancellationToken) =>
        audit.RecordAsync(new AuditRecord(action, "service_plan", plan.Id.ToString(), ScopePath.Parse(plan.Scope), new { plan.Title, plan.Date }), cancellationToken);
}

/// <summary>Builds plan views: items with start times and song names, needs against who's scheduled, and the people.</summary>
public sealed class PlanReader(IServicesDb db, IPeopleDirectory people)
{
    public async Task<PlanDto> DetailAsync(Plan plan, CancellationToken cancellationToken)
    {
        var items = await ItemsAsync(plan, cancellationToken);
        var assignments = await AssignmentsAsync([plan.Id], cancellationToken);
        var positionIds = plan.Needs.Select(n => n.PositionId).Concat(assignments.Select(a => a.PositionId)).Distinct().ToList();
        var positions = await db.Positions.AsNoTracking().Where(p => positionIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);
        var teamIds = positions.Values.Select(p => p.TeamId).Distinct().ToList();
        var teams = await db.Teams.AsNoTracking().Where(t => teamIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name, cancellationToken);

        var needs = positionIds
            .Where(positions.ContainsKey)
            .Select(id =>
            {
                var position = positions[id];
                var here = assignments.Where(a => a.PositionId == id).ToList();
                return new NeedDto(
                    position.TeamId,
                    teams.GetValueOrDefault(position.TeamId) ?? "Team",
                    id,
                    position.Name,
                    plan.Needs.FirstOrDefault(n => n.PositionId == id)?.Count ?? 0,
                    here.Count(a => a.Status == AssignmentStatus.Accepted),
                    here.Count(a => a.Status == AssignmentStatus.Pending));
            })
            .OrderBy(n => n.Team).ThenBy(n => positions[n.PositionId].Order)
            .ToList();

        var end = plan.StartTime.AddMinutes(plan.TotalSeconds / 60d);
        return new PlanDto(plan.Id, plan.ServiceTypeId, plan.Title, plan.Date, plan.StartTime, end, plan.Scope, plan.SeriesTitle, plan.Notes, plan.LivestreamId,
            items, needs, assignments, plan.LiveItemId, plan.UpdatedAt);
    }

    public async Task<List<PlanItemDto>> ItemsAsync(Plan plan, CancellationToken cancellationToken)
    {
        var songIds = plan.Items.Where(i => i.SongId is not null).Select(i => i.SongId!.Value).Distinct().ToList();
        var songs = await db.Songs.AsNoTracking().Where(s => songIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, cancellationToken);
        var times = plan.StartTimes();
        return plan.Items.Select(i =>
        {
            var song = i.SongId is { } id ? songs.GetValueOrDefault(id) : null;
            var arrangement = song?.Arrangements.FirstOrDefault(a => a.Id == i.ArrangementId);
            return new PlanItemDto(i, times[i.Id], song?.Title, arrangement?.Name);
        }).ToList();
    }

    public async Task<List<AssignmentDto>> AssignmentsAsync(IReadOnlyCollection<Guid> planIds, CancellationToken cancellationToken)
    {
        var assignments = await db.Assignments.AsNoTracking().Where(a => planIds.Contains(a.PlanId)).ToListAsync(cancellationToken);
        var positionIds = assignments.Select(a => a.PositionId).Distinct().ToList();
        var positions = await db.Positions.AsNoTracking().Where(p => positionIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);
        var teamIds = assignments.Select(a => a.TeamId).Distinct().ToList();
        var teams = await db.Teams.AsNoTracking().Where(t => teamIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name, cancellationToken);
        var names = await people.GetManyAsync(assignments.Select(a => a.PersonId).Distinct().ToList(), cancellationToken);
        return assignments
            .Select(a => new AssignmentDto(
                a.Id,
                a.PlanId,
                a.TeamId,
                teams.GetValueOrDefault(a.TeamId) ?? "Team",
                a.PositionId,
                positions.GetValueOrDefault(a.PositionId) ?? "Position",
                a.PersonId,
                names.GetValueOrDefault(a.PersonId)?.DisplayName ?? "Unknown",
                a.Status,
                a.DeclineReason,
                a.RequestedAt,
                a.RespondedAt))
            .OrderBy(a => a.Team).ThenBy(a => a.Position).ThenBy(a => a.Name)
            .ToList();
    }

    public async Task<List<PlanSummaryDto>> SummariesAsync(IReadOnlyList<Plan> plans, CancellationToken cancellationToken)
    {
        var ids = plans.Select(p => p.Id).ToList();
        var counts = await db.Assignments.AsNoTracking().Where(a => ids.Contains(a.PlanId))
            .GroupBy(a => new { a.PlanId, a.Status })
            .Select(g => new { g.Key.PlanId, g.Key.Status, Count = g.Count() })
            .ToListAsync(cancellationToken);
        int Count(Guid planId, AssignmentStatus status) => counts.FirstOrDefault(c => c.PlanId == planId && c.Status == status)?.Count ?? 0;
        return plans.Select(p => new PlanSummaryDto(
                p.Id,
                p.Title,
                p.Date,
                p.StartTime,
                p.SeriesTitle,
                p.Needs.Sum(n => n.Count),
                Count(p.Id, AssignmentStatus.Accepted),
                Count(p.Id, AssignmentStatus.Pending),
                Count(p.Id, AssignmentStatus.Declined),
                p.IsLive))
            .ToList();
    }
}
