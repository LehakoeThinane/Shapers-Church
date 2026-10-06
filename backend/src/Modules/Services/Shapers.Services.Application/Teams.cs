using Microsoft.EntityFrameworkCore;
using Shapers.Church.Contracts;
using Shapers.People.Contracts;
using Shapers.Platform.Auditing;
using Shapers.Platform.Authorization;
using Shapers.Services.Contracts;
using Shapers.Services.Domain;

namespace Shapers.Services.Application;

/// <summary>The church's calendar day (Johannesburg, UTC+2) and other small shared helpers.</summary>
public static class ServingTime
{
    private static readonly TimeSpan Johannesburg = TimeSpan.FromHours(2);

    public static DateOnly Today(TimeProvider clock) => DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(Johannesburg).DateTime);

    public static async Task<IReadOnlyList<ScopePath>> ScopesAsync(IAuthorizer authorizer, CancellationToken cancellationToken, params string[] permissions)
    {
        var scopes = new List<ScopePath>();
        foreach (var permission in permissions)
        {
            scopes.AddRange(await authorizer.ScopesForAsync(permission, cancellationToken));
        }

        return ScopeSet.Collapse(scopes);
    }

    public static async Task<bool> CanAnyAsync(IAuthorizer authorizer, string scope, CancellationToken cancellationToken, params string[] permissions)
    {
        var path = ScopePath.Parse(scope);
        foreach (var permission in permissions)
        {
            if (await authorizer.CanAsync(permission, path, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The scope a new team, plan or song goes in: the one asked for (if allowed), else the widest one the user has.</summary>
    public static async Task<Result<ScopePath>> ChooseScopeAsync(IAuthorizer authorizer, IChurchDirectory church, string permission, string? wanted, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(wanted))
        {
            return ScopePath.TryParse(wanted, out var scope) && await church.ScopeExistsAsync(scope.Value, cancellationToken) && await authorizer.CanAsync(permission, scope, cancellationToken)
                ? scope
                : Error.Forbidden("services.scope_forbidden", "You can't add that there.");
        }

        var scopes = await authorizer.ScopesForAsync(permission, cancellationToken);
        return scopes.Count == 0 ? Error.Forbidden("services.forbidden", "You don't have access to do that.") : ScopeSet.Collapse(scopes)[0];
    }
}

/// <summary>Serving teams, their positions and members. Needs services.schedule at the team's scope.</summary>
public sealed class TeamService(IServicesDb db, IPeopleDirectory people, IChurchDirectory church, IAuthorizer authorizer, IAuditLog audit, TimeProvider clock)
{
    private static readonly Error NotFound = Error.NotFound("services.team_not_found", "Team not found.");

    public async Task<IReadOnlyList<TeamDto>> ListAsync(bool includeArchived, CancellationToken cancellationToken)
    {
        var scopes = await ServingTime.ScopesAsync(authorizer, cancellationToken, ServicesPermissions.Schedule, ServicesPermissions.PlansEdit);
        var teams = await db.Teams.AsNoTracking().WithinScopes(t => t.Scope, scopes)
            .Where(t => includeArchived || !t.IsArchived)
            .OrderBy(t => t.Name)
            .ToListAsync(cancellationToken);
        return await ToDtosAsync(teams, cancellationToken);
    }

    public async Task<Result<TeamDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var team = await db.Teams.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        return team is null || !await ServingTime.CanAnyAsync(authorizer, team.Scope, cancellationToken, ServicesPermissions.Schedule, ServicesPermissions.PlansEdit)
            ? NotFound
            : (await ToDtosAsync([team], cancellationToken))[0];
    }

    public async Task<Result<TeamDto>> CreateAsync(SaveTeamRequest request, CancellationToken cancellationToken)
    {
        var scope = await ServingTime.ChooseScopeAsync(authorizer, church, ServicesPermissions.Schedule, request.Scope, cancellationToken);
        if (scope.IsFailure)
        {
            return scope.Error!;
        }

        if (await db.Teams.AnyAsync(t => t.Scope == scope.Value.Value && t.Name == request.Name.Trim() && !t.IsArchived, cancellationToken))
        {
            return Error.Conflict("services.team_exists", "There's already a team with that name here.");
        }

        var team = Team.Create(request.Name, scope.Value, request.Description, request.OpenToMinors, clock.GetUtcNow());
        db.Teams.Add(team);
        await db.SaveChangesAsync(cancellationToken);
        await AuditAsync("services.team.created", team, cancellationToken);
        return (await ToDtosAsync([team], cancellationToken))[0];
    }

    public Task<Result<TeamDto>> UpdateAsync(Guid id, SaveTeamRequest request, CancellationToken cancellationToken) =>
        ChangeAsync(id, "services.team.updated", (team, _) =>
        {
            team.Update(request.Name, request.Description, request.OpenToMinors);
            return Task.FromResult(Result.Success());
        }, cancellationToken);

    public Task<Result<TeamDto>> ArchiveAsync(Guid id, bool archive, CancellationToken cancellationToken) =>
        ChangeAsync(id, archive ? "services.team.archived" : "services.team.restored", (team, _) =>
        {
            if (archive)
            {
                team.Archive();
            }
            else
            {
                team.Restore();
            }

            return Task.FromResult(Result.Success());
        }, cancellationToken);

    public Task<Result<TeamDto>> AddPositionAsync(Guid id, SavePositionRequest request, CancellationToken cancellationToken) =>
        ChangeAsync(id, "services.position.added", async (team, ct) =>
        {
            if (await db.Positions.AnyAsync(p => p.TeamId == team.Id && p.Name == request.Name.Trim() && !p.IsArchived, ct))
            {
                return Error.Conflict("services.position_exists", "The team already has that position.");
            }

            db.Positions.Add(TeamPosition.Create(team.Id, request.Name, request.Order));
            return Result.Success();
        }, cancellationToken);

    public Task<Result<TeamDto>> UpdatePositionAsync(Guid id, Guid positionId, SavePositionRequest request, CancellationToken cancellationToken) =>
        ChangeAsync(id, "services.position.updated", async (team, ct) =>
        {
            var position = await db.Positions.SingleOrDefaultAsync(p => p.Id == positionId && p.TeamId == team.Id, ct);
            if (position is null)
            {
                return Error.NotFound("services.position_not_found", "Position not found.");
            }

            position.Rename(request.Name, request.Order);
            return Result.Success();
        }, cancellationToken);

    public Task<Result<TeamDto>> ArchivePositionAsync(Guid id, Guid positionId, CancellationToken cancellationToken) =>
        ChangeAsync(id, "services.position.archived", async (team, ct) =>
        {
            var position = await db.Positions.SingleOrDefaultAsync(p => p.Id == positionId && p.TeamId == team.Id, ct);
            if (position is null)
            {
                return Error.NotFound("services.position_not_found", "Position not found.");
            }

            position.Archive();
            return Result.Success();
        }, cancellationToken);

    /// <summary>Adds someone to the team or changes their positions.</summary>
    public Task<Result<TeamDto>> SaveMemberAsync(Guid id, SaveMemberRequest request, CancellationToken cancellationToken) =>
        ChangeAsync(id, "services.member.saved", async (team, ct) =>
        {
            if (await people.GetAsync(request.PersonId, ct) is null)
            {
                return Error.NotFound("services.person_not_found", "Person not found.");
            }

            if (!team.OpenToMinors && await people.IsMinorAsync(request.PersonId, ct))
            {
                return new Error("services.minor_not_allowed", $"{team.Name} isn't open to under-18s. Mark the team as open to minors first if that's intended.");
            }

            var positions = await db.Positions.Where(p => p.TeamId == team.Id && !p.IsArchived).Select(p => p.Id).ToListAsync(ct);
            if (request.PositionIds.Any(p => !positions.Contains(p)))
            {
                return new Error("services.position_not_on_team", "Choose positions from this team.");
            }

            var member = await db.Members.SingleOrDefaultAsync(m => m.TeamId == team.Id && m.PersonId == request.PersonId, ct);
            if (member is null)
            {
                db.Members.Add(TeamMember.Add(team.Id, request.PersonId, request.PositionIds, request.IsLeader, clock.GetUtcNow()));
            }
            else
            {
                member.Update(request.PositionIds, request.IsLeader);
            }

            return Result.Success();
        }, cancellationToken);

    public Task<Result<TeamDto>> RemoveMemberAsync(Guid id, Guid personId, CancellationToken cancellationToken) =>
        ChangeAsync(id, "services.member.removed", async (team, ct) =>
        {
            await db.Members.Where(m => m.TeamId == team.Id && m.PersonId == personId).ExecuteDeleteAsync(ct);
            return Result.Success();
        }, cancellationToken);

    private async Task<Result<TeamDto>> ChangeAsync(Guid id, string action, Func<Team, CancellationToken, Task<Result>> change, CancellationToken cancellationToken)
    {
        var team = await db.Teams.SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (team is null || !await authorizer.CanAsync(ServicesPermissions.Schedule, ScopePath.Parse(team.Scope), cancellationToken))
        {
            return NotFound;
        }

        var result = await change(team, cancellationToken);
        if (result.IsFailure)
        {
            return result.Error!;
        }

        await db.SaveChangesAsync(cancellationToken);
        await AuditAsync(action, team, cancellationToken);
        return (await ToDtosAsync([team], cancellationToken))[0];
    }

    private async Task<List<TeamDto>> ToDtosAsync(IReadOnlyList<Team> teams, CancellationToken cancellationToken)
    {
        var ids = teams.Select(t => t.Id).ToList();
        var positions = await db.Positions.AsNoTracking().Where(p => ids.Contains(p.TeamId) && !p.IsArchived).OrderBy(p => p.Order).ThenBy(p => p.Name).ToListAsync(cancellationToken);
        var members = await db.Members.AsNoTracking().Where(m => ids.Contains(m.TeamId)).ToListAsync(cancellationToken);
        var names = await people.GetManyAsync(members.Select(m => m.PersonId).Distinct().ToList(), cancellationToken);
        return teams.Select(t => new TeamDto(
                t.Id,
                t.Name,
                t.Description,
                t.OpenToMinors,
                t.Scope,
                t.IsArchived,
                positions.Where(p => p.TeamId == t.Id).Select(p => new PositionDto(p.Id, p.Name, p.Order)).ToList(),
                members.Where(m => m.TeamId == t.Id)
                    .Select(m => new TeamMemberDto(m.PersonId, names.GetValueOrDefault(m.PersonId)?.DisplayName ?? "Unknown", m.PositionIds, m.IsLeader))
                    .OrderByDescending(m => m.IsLeader).ThenBy(m => m.Name)
                    .ToList()))
            .ToList();
    }

    private Task AuditAsync(string action, Team team, CancellationToken cancellationToken) =>
        audit.RecordAsync(new AuditRecord(action, "serving_team", team.Id.ToString(), ScopePath.Parse(team.Scope), new { team.Name }), cancellationToken);
}
