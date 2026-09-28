using Microsoft.EntityFrameworkCore;
using Shapers.People.Contracts;
using Shapers.People.Domain;
using Shapers.Platform.Authorization;

namespace Shapers.People.Application;

public interface IPeopleDb
{
    DbSet<Person> Persons { get; }

    DbSet<Household> Households { get; }

    DbSet<MembershipStatus> MembershipStatuses { get; }

    DbSet<ConsentRecord> ConsentRecords { get; }

    DbSet<DuplicateCandidate> DuplicateCandidates { get; }

    DbSet<PersonMerge> PersonMerges { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public sealed class PeoplePermissionProvider : IPermissionProvider
{
    public IEnumerable<PermissionDefinition> GetPermissions() =>
    [
        new(PeoplePermissions.ProfilesView, "people", "View people and households", IsSensitive: true),
        new(PeoplePermissions.ProfilesEdit, "people", "Add and edit people, households and consent records", IsSensitive: true),
        new(PeoplePermissions.ProfilesMerge, "people", "Merge duplicate records", IsSensitive: true),
        new(PeoplePermissions.StatusesManage, "people", "Configure membership statuses"),
    ];
}

public static class PeopleAudit
{
    public const string EntityPerson = "person";
    public const string EntityHousehold = "household";
}
