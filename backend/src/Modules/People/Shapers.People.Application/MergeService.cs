using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Shapers.Church.Contracts;
using Shapers.Identity.Contracts;
using Shapers.People.Contracts;
using Shapers.People.Domain;
using Shapers.Platform.Auditing;
using Shapers.Platform.Authorization;
using Shapers.Platform.Text;

namespace Shapers.People.Application;

/// <summary>Finds likely duplicates and records them for review. Never merges on its own.</summary>
public sealed class DuplicateDetector(IPeopleDb db, TimeProvider clock)
{
    private const int MaxCandidates = 50;

    public async Task<int> DetectAsync(Person person, CancellationToken cancellationToken)
    {
        var values = person.Contacts.Select(c => c.Value).ToList();
        var lastName = LikePattern.Escape(person.LastName);

        var possible = await db.Persons.AsNoTracking()
            .Where(p => p.Id != person.Id && p.Status != PersonStatus.Merged)
            .Where(p => p.Contacts.Any(c => values.Contains(c.Value)) || EF.Functions.ILike(p.LastName, lastName))
            .OrderByDescending(p => p.CreatedAt)
            .Take(MaxCandidates)
            .ToListAsync(cancellationToken);

        var found = 0;
        foreach (var other in possible)
        {
            var match = DuplicateMatcher.Compare(person, other);
            if (!match.IsLikely)
            {
                continue;
            }

            var candidate = DuplicateCandidate.Create(person.Id, other.Id, match, clock.GetUtcNow());
            var exists = await db.DuplicateCandidates.AnyAsync(
                d => d.PersonAId == candidate.PersonAId && d.PersonBId == candidate.PersonBId, cancellationToken);
            if (!exists)
            {
                db.DuplicateCandidates.Add(candidate);
                found++;
            }
        }

        if (found > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return found;
    }
}

public sealed class MergeService(
    IPeopleDb db,
    IAuthorizer authorizer,
    ICurrentUser currentUser,
    IUserDirectory users,
    IChurchDirectory church,
    IAuditLog audit,
    PersonDetailBuilder builder,
    TimeProvider clock)
{
    public async Task<IReadOnlyList<DuplicateCandidateDto>> ListDuplicatesAsync(CancellationToken cancellationToken)
    {
        var scopes = await authorizer.ScopesForAsync(PeoplePermissions.ProfilesMerge, cancellationToken);
        if (scopes.Count == 0)
        {
            return [];
        }

        var candidates = await db.DuplicateCandidates.AsNoTracking()
            .Where(d => d.Status == DuplicateStatus.Open)
            .OrderByDescending(d => d.Score).ThenBy(d => d.DetectedAt)
            .Take(200)
            .ToListAsync(cancellationToken);

        var ids = candidates.SelectMany(d => new[] { d.PersonAId, d.PersonBId }).Distinct().ToList();
        var people = await db.Persons.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);
        var statuses = await builder.StatusesAsync(cancellationToken);
        var campuses = await church.GetCampusesAsync(cancellationToken);

        bool Visible(Person p) => scopes.Any(s => s.Covers(ScopePath.Parse(p.Scope)));

        return candidates
            .Where(d => people.ContainsKey(d.PersonAId) && people.ContainsKey(d.PersonBId))
            .Where(d => Visible(people[d.PersonAId]) && Visible(people[d.PersonBId]))
            .Select(d => new DuplicateCandidateDto(
                d.Id,
                PersonDetailBuilder.ToListItem(people[d.PersonAId], statuses, campuses),
                PersonDetailBuilder.ToListItem(people[d.PersonBId], statuses, campuses),
                d.Score,
                d.Reasons,
                d.DetectedAt))
            .ToList();
    }

    public async Task<Result> DismissAsync(Guid candidateId, CancellationToken cancellationToken)
    {
        var candidate = await db.DuplicateCandidates.SingleOrDefaultAsync(d => d.Id == candidateId, cancellationToken);
        if (candidate is null || candidate.Status != DuplicateStatus.Open)
        {
            return Error.NotFound("people.duplicate_not_found", "Duplicate suggestion not found.");
        }

        var scopes = await db.Persons.AsNoTracking()
            .Where(p => p.Id == candidate.PersonAId || p.Id == candidate.PersonBId)
            .Select(p => p.Scope)
            .ToListAsync(cancellationToken);
        foreach (var scope in scopes)
        {
            if (!await authorizer.CanAsync(PeoplePermissions.ProfilesMerge, ScopePath.Parse(scope), cancellationToken))
            {
                return Error.NotFound("people.duplicate_not_found", "Duplicate suggestion not found.");
            }
        }

        candidate.Resolve(DuplicateStatus.Dismissed, currentUser.UserId, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<PersonDetailDto>> MergeAsync(MergeRequest request, CancellationToken cancellationToken)
    {
        var survivor = await db.Persons.SingleOrDefaultAsync(p => p.Id == request.SurvivorId, cancellationToken);
        var duplicate = await db.Persons.SingleOrDefaultAsync(p => p.Id == request.DuplicateId, cancellationToken);
        if (survivor is null || duplicate is null
            || !await authorizer.CanAsync(PeoplePermissions.ProfilesMerge, ScopePath.Parse(survivor.Scope), cancellationToken)
            || !await authorizer.CanAsync(PeoplePermissions.ProfilesMerge, ScopePath.Parse(duplicate.Scope), cancellationToken))
        {
            return Error.NotFound("people.not_found", "Person not found.");
        }

        // Two logins can't become one person automatically: someone would lose access to their account.
        var withLogins = await users.PeopleWithUsersAsync([survivor.Id, duplicate.Id], cancellationToken);
        if (withLogins.Count == 2)
        {
            return Error.Conflict(
                "people.merge_two_logins",
                "Both records have their own login. Remove one login before merging.");
        }

        var households = await db.Households
            .Where(h => h.Members.Any(m => m.PersonId == duplicate.Id))
            .ToListAsync(cancellationToken);

        var snapshot = JsonSerializer.Serialize(await builder.BuildAsync(duplicate, cancellationToken), JsonSerializerOptions.Web);
        var merge = PersonMerger.Merge(survivor, duplicate, households, snapshot, currentUser.UserId, clock.GetUtcNow());
        db.PersonMerges.Add(merge);

        var (a, b) = survivor.Id.CompareTo(duplicate.Id) < 0 ? (survivor.Id, duplicate.Id) : (duplicate.Id, survivor.Id);
        var candidates = await db.DuplicateCandidates
            .Where(d => d.Status == DuplicateStatus.Open && d.PersonAId == a && d.PersonBId == b)
            .ToListAsync(cancellationToken);
        foreach (var candidate in candidates)
        {
            candidate.Resolve(DuplicateStatus.Merged, currentUser.UserId, clock.GetUtcNow());
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(
            new AuditRecord("people.person.merged", PeopleAudit.EntityPerson, survivor.Id.ToString(), ScopePath.Parse(survivor.Scope),
                new { mergedId = duplicate.Id }),
            cancellationToken);

        return await builder.BuildAsync(survivor, cancellationToken);
    }
}
