using Microsoft.EntityFrameworkCore;
using Shapers.Church.Contracts;
using Shapers.People.Contracts;
using Shapers.People.Domain;
using Shapers.Platform.Auditing;
using Shapers.Platform.Authorization;
using Shapers.Platform.Text;
using Shapers.Platform.Web;

namespace Shapers.People.Application;

/// <summary>Staff-facing use cases for the church database. Every call checks permissions at the record's scope.</summary>
public sealed class PeopleService(
    IPeopleDb db,
    IAuthorizer authorizer,
    ICurrentUser currentUser,
    IChurchDirectory church,
    IAuditLog audit,
    PersonDetailBuilder builder,
    DuplicateDetector duplicates,
    TimeProvider clock)
{
    private static readonly Error PersonNotFound = Error.NotFound("people.not_found", "Person not found.");

    public async Task<PagedResult<PersonListItemDto>> ListAsync(PeopleListQuery request, CancellationToken cancellationToken)
    {
        var paging = new PageRequest(request.Page, request.PageSize);
        var scopes = await authorizer.ScopesForAsync(PeoplePermissions.ProfilesView, cancellationToken);

        var query = db.Persons.AsNoTracking()
            .WithinScopes(p => p.Scope, scopes)
            .Where(p => p.Status != PersonStatus.Merged);

        if (ScopePath.TryParse(request.Scope, out var within))
        {
            query = query.WithinScopes(p => p.Scope, [within]);
        }

        if (!request.IncludeInactive)
        {
            query = query.Where(p => p.Status == PersonStatus.Active);
        }

        if (request.MembershipStatusId is { } statusId)
        {
            query = query.Where(p => p.MembershipStatusId == statusId);
        }

        foreach (var term in (request.Search ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(5))
        {
            if (ContactNormaliser.TryNormalisePhone(term, out var phone) && term.Any(char.IsDigit) && term.Length >= 9)
            {
                query = query.Where(p => p.Contacts.Any(c => c.Value == phone));
                continue;
            }

            var pattern = LikePattern.Contains(term);
            query = query.Where(p =>
                EF.Functions.ILike(p.FirstName, pattern) ||
                EF.Functions.ILike(p.LastName, pattern) ||
                (p.PreferredName != null && EF.Functions.ILike(p.PreferredName, pattern)) ||
                p.Contacts.Any(c => EF.Functions.ILike(c.Value, pattern)));
        }

        var total = await query.CountAsync(cancellationToken);
        var people = await query
            .OrderBy(p => p.LastName).ThenBy(p => p.FirstName).ThenBy(p => p.Id)
            .Skip(paging.Skip)
            .Take(paging.SafePageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<PersonListItemDto>(await builder.BuildListAsync(people, cancellationToken), paging.SafePage, paging.SafePageSize, total);
    }

    public async Task<Result<PersonDetailDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var person = await db.Persons.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

        // Out-of-scope records look exactly like missing ones, so staff can't probe for who attends.
        if (person is null || !await authorizer.CanAsync(PeoplePermissions.ProfilesView, ScopePath.Parse(person.Scope), cancellationToken))
        {
            return PersonNotFound;
        }

        await audit.RecordAsync(
            new AuditRecord("people.person.viewed", PeopleAudit.EntityPerson, id.ToString(), ScopePath.Parse(person.Scope), IsSensitiveRead: true),
            cancellationToken);
        return await builder.BuildAsync(person, cancellationToken);
    }

    public async Task<Result<PersonDetailDto>> CreateAsync(CreatePersonRequest request, CancellationToken cancellationToken)
    {
        var scope = await ResolveCampusScopeAsync(request.CampusId, cancellationToken);
        if (scope is null)
        {
            return Error.NotFound("people.campus_not_found", "Campus not found.");
        }

        if (!await authorizer.CanAsync(PeoplePermissions.ProfilesEdit, scope, cancellationToken))
        {
            return Error.Forbidden("people.forbidden", "You cannot add people to this campus.");
        }

        var status = request.MembershipStatusId is { } statusId
            ? await db.MembershipStatuses.SingleOrDefaultAsync(s => s.Id == statusId && s.IsActive, cancellationToken)
            : await db.MembershipStatuses.SingleAsync(s => s.IsDefault, cancellationToken);
        if (status is null)
        {
            return Error.NotFound("people.status_not_found", "Membership status not found.");
        }

        string? email = null;
        if (!string.IsNullOrWhiteSpace(request.Email) && !ContactNormaliser.TryNormaliseEmail(request.Email, out email))
        {
            return new Error("people.email_invalid", "That email address doesn't look right.");
        }

        string? mobile = null;
        if (!string.IsNullOrWhiteSpace(request.Mobile) && !ContactNormaliser.TryNormalisePhone(request.Mobile, out mobile))
        {
            return new Error("people.mobile_invalid", "That mobile number doesn't look right.");
        }

        var now = clock.GetUtcNow();
        var person = Person.Create(scope, request.FirstName, request.LastName, status, PersonSource.Admin, now, currentUser.UserId);
        person.Rename(request.FirstName, request.LastName, request.PreferredName, now);
        person.SetDemographics(request.DateOfBirth, request.Gender, now);
        if (email is not null)
        {
            person.AddContact(ContactType.Email, email, isPrimary: true, isVerified: false, now);
        }

        if (mobile is not null)
        {
            person.AddContact(ContactType.Mobile, mobile, isPrimary: true, isVerified: false, now);
        }

        db.Persons.Add(person);
        await db.SaveChangesAsync(cancellationToken);
        await duplicates.DetectAsync(person, cancellationToken);

        await audit.RecordAsync(new AuditRecord("people.person.created", PeopleAudit.EntityPerson, person.Id.ToString(), scope), cancellationToken);
        return await builder.BuildAsync(person, cancellationToken);
    }

    public Task<Result<PersonDetailDto>> UpdateAsync(Guid id, UpdatePersonRequest request, CancellationToken cancellationToken) =>
        EditAsync(id, "people.person.updated", person =>
        {
            var now = clock.GetUtcNow();
            person.Rename(request.FirstName, request.LastName, request.PreferredName, now);
            person.SetDemographics(request.DateOfBirth, request.Gender, now);
            return Task.FromResult(Result.Success());
        }, cancellationToken);

    public Task<Result<PersonDetailDto>> AddContactAsync(Guid id, AddContactRequest request, CancellationToken cancellationToken) =>
        EditAsync(id, "people.contact.added", person =>
        {
            string value;
            var valid = request.Type == ContactType.Email
                ? ContactNormaliser.TryNormaliseEmail(request.Value, out value)
                : ContactNormaliser.TryNormalisePhone(request.Value, out value);
            if (!valid)
            {
                return Task.FromResult(Result.Failure(new Error("people.contact_invalid", $"That {request.Type} doesn't look right.")));
            }

            person.AddContact(request.Type, value, request.IsPrimary, isVerified: false, clock.GetUtcNow());
            return Task.FromResult(Result.Success());
        }, cancellationToken);

    public Task<Result<PersonDetailDto>> RemoveContactAsync(Guid id, Guid contactId, CancellationToken cancellationToken) =>
        EditAsync(id, "people.contact.removed", person =>
        {
            person.RemoveContact(contactId, clock.GetUtcNow());
            return Task.FromResult(Result.Success());
        }, cancellationToken);

    public Task<Result<PersonDetailDto>> ChangeStatusAsync(Guid id, ChangeStatusRequest request, CancellationToken cancellationToken) =>
        EditAsync(id, "people.status.changed", async person =>
        {
            var status = await db.MembershipStatuses.SingleOrDefaultAsync(s => s.Id == request.MembershipStatusId && s.IsActive, cancellationToken);
            if (status is null)
            {
                return Error.NotFound("people.status_not_found", "Membership status not found.");
            }

            var now = clock.GetUtcNow();
            person.ChangeMembershipStatus(status, request.EffectiveDate ?? DateOnly.FromDateTime(now.UtcDateTime), currentUser.UserId, now);
            return Result.Success();
        }, cancellationToken);

    public Task<Result<PersonDetailDto>> MoveCampusAsync(Guid id, MoveCampusRequest request, CancellationToken cancellationToken) =>
        EditAsync(id, "people.person.moved", async person =>
        {
            var target = await ResolveCampusScopeAsync(request.CampusId, cancellationToken);
            if (target is null)
            {
                return Error.NotFound("people.campus_not_found", "Campus not found.");
            }

            // Moving someone needs edit rights at both ends, or it would be a way to hide or steal records.
            if (!await authorizer.CanAsync(PeoplePermissions.ProfilesEdit, target, cancellationToken))
            {
                return Error.Forbidden("people.forbidden", "You cannot move people into that campus.");
            }

            person.MoveTo(target, clock.GetUtcNow());
            return Result.Success();
        }, cancellationToken);

    public async Task<Result<IReadOnlyList<ConsentDto>>> RecordConsentAsync(Guid id, RecordConsentRequest request, CancellationToken cancellationToken)
    {
        var person = await db.Persons.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (person is null || !await authorizer.CanAsync(PeoplePermissions.ProfilesEdit, ScopePath.Parse(person.Scope), cancellationToken))
        {
            return PersonNotFound;
        }

        var now = clock.GetUtcNow();
        foreach (var decision in request.Decisions)
        {
            db.ConsentRecords.Add(ConsentRecord.Record(
                id, decision.Purpose, decision.Granted, request.LawfulBasis, request.PolicyVersion, request.Source, now, currentUser.UserId));
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(
            new AuditRecord("people.consent.recorded", PeopleAudit.EntityPerson, id.ToString(), ScopePath.Parse(person.Scope),
                new { purposes = request.Decisions.Select(d => new { d.Purpose, d.Granted }) }),
            cancellationToken);

        var history = await db.ConsentRecords.AsNoTracking().Where(c => c.PersonId == id).ToListAsync(cancellationToken);
        return ConsentRecord.Current(history).Values
            .Select(c => new ConsentDto(c.Purpose, c.Granted, c.LawfulBasis, c.PolicyVersion, c.Source, c.RecordedAt))
            .ToList();
    }

    public async Task<IReadOnlyList<MembershipStatusDto>> ListStatusesAsync(CancellationToken cancellationToken) =>
        (await db.MembershipStatuses.AsNoTracking().OrderBy(s => s.SortOrder).ToListAsync(cancellationToken))
            .Select(PersonDetailBuilder.ToDto)
            .ToList();

    public async Task<Result<MembershipStatusDto>> CreateStatusAsync(CreateMembershipStatusRequest request, CancellationToken cancellationToken)
    {
        var root = await church.GetRootScopeAsync(cancellationToken);
        if (!await authorizer.CanAsync(PeoplePermissions.StatusesManage, ScopePath.Parse(root.Path), cancellationToken))
        {
            return Error.Forbidden("people.forbidden", "Only church-wide administrators can configure statuses.");
        }

        var status = MembershipStatus.Create(request.Name, request.Stage, request.SortOrder);
        db.MembershipStatuses.Add(status);
        await db.SaveChangesAsync(cancellationToken);
        return PersonDetailBuilder.ToDto(status);
    }

    private async Task<Result<PersonDetailDto>> EditAsync(
        Guid id,
        string auditAction,
        Func<Person, Task<Result>> change,
        CancellationToken cancellationToken)
    {
        var person = await db.Persons.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (person is null || !await authorizer.CanAsync(PeoplePermissions.ProfilesEdit, ScopePath.Parse(person.Scope), cancellationToken))
        {
            return PersonNotFound;
        }

        var result = await change(person);
        if (result.IsFailure)
        {
            return result.Error!;
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord(auditAction, PeopleAudit.EntityPerson, id.ToString(), ScopePath.Parse(person.Scope)), cancellationToken);
        return await builder.BuildAsync(person, cancellationToken);
    }

    private async Task<ScopePath?> ResolveCampusScopeAsync(Guid? campusId, CancellationToken cancellationToken)
    {
        if (campusId is { } id)
        {
            var campus = await church.GetCampusAsync(id, cancellationToken);
            return campus is null ? null : ScopePath.Parse(campus.Scope);
        }

        var campuses = await church.GetCampusesAsync(cancellationToken);
        var primary = campuses.FirstOrDefault(c => c.IsPrimary) ?? (campuses.Count > 0 ? campuses[0] : null);
        return primary is not null
            ? ScopePath.Parse(primary.Scope)
            : ScopePath.Parse((await church.GetRootScopeAsync(cancellationToken)).Path);
    }
}
