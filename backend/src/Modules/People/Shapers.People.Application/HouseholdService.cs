using Microsoft.EntityFrameworkCore;
using Shapers.Church.Contracts;
using Shapers.People.Contracts;
using Shapers.People.Domain;
using Shapers.Platform.Auditing;
using Shapers.Platform.Authorization;

namespace Shapers.People.Application;

public sealed class HouseholdService(
    IPeopleDb db,
    IAuthorizer authorizer,
    IChurchDirectory church,
    IAuditLog audit,
    TimeProvider clock)
{
    private static readonly Error HouseholdNotFound = Error.NotFound("people.household_not_found", "Household not found.");

    public async Task<Result<HouseholdDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var household = await db.Households.AsNoTracking().SingleOrDefaultAsync(h => h.Id == id, cancellationToken);
        if (household is null || !await authorizer.CanAsync(PeoplePermissions.ProfilesView, ScopePath.Parse(household.Scope), cancellationToken))
        {
            return HouseholdNotFound;
        }

        return await ToDtoAsync(household, cancellationToken);
    }

    public async Task<Result<HouseholdDto>> CreateAsync(CreateHouseholdRequest request, CancellationToken cancellationToken)
    {
        ScopePath scope;
        if (request.CampusId is { } campusId)
        {
            var campus = await church.GetCampusAsync(campusId, cancellationToken);
            if (campus is null)
            {
                return Error.NotFound("people.campus_not_found", "Campus not found.");
            }

            scope = ScopePath.Parse(campus.Scope);
        }
        else
        {
            var campuses = await church.GetCampusesAsync(cancellationToken);
            scope = ScopePath.Parse((campuses.FirstOrDefault(c => c.IsPrimary) ?? campuses[0]).Scope);
        }

        if (!await authorizer.CanAsync(PeoplePermissions.ProfilesEdit, scope, cancellationToken))
        {
            return Error.Forbidden("people.forbidden", "You cannot add households to this campus.");
        }

        var household = Household.Create(request.Name, scope, clock.GetUtcNow());
        foreach (var member in request.Members)
        {
            var check = await EnsureCanEditPersonAsync(member.PersonId, cancellationToken);
            if (check.IsFailure)
            {
                return check.Error!;
            }

            household.AddMember(member.PersonId, member.Role);
        }

        db.Households.Add(household);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord("people.household.created", PeopleAudit.EntityHousehold, household.Id.ToString(), scope), cancellationToken);
        return await ToDtoAsync(household, cancellationToken);
    }

    public Task<Result<HouseholdDto>> AddMemberAsync(Guid id, HouseholdMemberRequest request, CancellationToken cancellationToken) =>
        EditAsync(id, "people.household.member_added", async household =>
        {
            var check = await EnsureCanEditPersonAsync(request.PersonId, cancellationToken);
            if (check.IsSuccess)
            {
                household.AddMember(request.PersonId, request.Role);
            }

            return check;
        }, cancellationToken);

    public Task<Result<HouseholdDto>> RemoveMemberAsync(Guid id, Guid personId, CancellationToken cancellationToken) =>
        EditAsync(id, "people.household.member_removed", household =>
        {
            household.RemoveMember(personId);
            return Task.FromResult(Result.Success());
        }, cancellationToken);

    public Task<Result<HouseholdDto>> SetPrimaryContactAsync(Guid id, Guid personId, CancellationToken cancellationToken) =>
        EditAsync(id, "people.household.primary_changed", household =>
        {
            household.SetPrimaryContact(personId);
            return Task.FromResult(Result.Success());
        }, cancellationToken);

    private async Task<Result<HouseholdDto>> EditAsync(Guid id, string auditAction, Func<Household, Task<Result>> change, CancellationToken cancellationToken)
    {
        var household = await db.Households.SingleOrDefaultAsync(h => h.Id == id, cancellationToken);
        if (household is null || !await authorizer.CanAsync(PeoplePermissions.ProfilesEdit, ScopePath.Parse(household.Scope), cancellationToken))
        {
            return HouseholdNotFound;
        }

        var result = await change(household);
        if (result.IsFailure)
        {
            return result.Error!;
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord(auditAction, PeopleAudit.EntityHousehold, id.ToString(), ScopePath.Parse(household.Scope)), cancellationToken);
        return await ToDtoAsync(household, cancellationToken);
    }

    private async Task<Result> EnsureCanEditPersonAsync(Guid personId, CancellationToken cancellationToken)
    {
        var person = await db.Persons.AsNoTracking()
            .Where(p => p.Id == personId && p.Status != PersonStatus.Merged)
            .Select(p => new { p.Scope })
            .SingleOrDefaultAsync(cancellationToken);
        return person is not null && await authorizer.CanAsync(PeoplePermissions.ProfilesEdit, ScopePath.Parse(person.Scope), cancellationToken)
            ? Result.Success()
            : Error.NotFound("people.not_found", "Person not found.");
    }

    private async Task<HouseholdDto> ToDtoAsync(Household household, CancellationToken cancellationToken)
    {
        var ids = household.Members.Select(m => m.PersonId).ToList();
        var names = await db.Persons.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, Name = (p.PreferredName ?? p.FirstName) + " " + p.LastName })
            .ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);
        return PersonDetailBuilder.ToDto(household, names);
    }
}
