using Shapers.SharedKernel;

namespace Shapers.Church.Contracts;

public static class ChurchPermissions
{
    public const string OrganisationManage = "church.organisation.manage";
    public const string CampusesManage = "church.campuses.manage";
    public const string MinistriesManage = "church.ministries.manage";
}

public sealed record CampusCreatedIntegrationEvent(Guid CampusId, string Name, string Scope) : IntegrationEvent;

public sealed record MinistryCreatedIntegrationEvent(Guid MinistryId, Guid? CampusId, string Name, string Scope) : IntegrationEvent;

public sealed record CampusSummary(Guid Id, string Name, string Scope, bool IsPrimary, string Status);

public sealed record ScopeSummary(string Path, string Name, string Type, Guid? EntityId);

/// <summary>Read-only view of the church structure for other modules.</summary>
public interface IChurchDirectory
{
    Task<ScopeSummary> GetRootScopeAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CampusSummary>> GetCampusesAsync(CancellationToken cancellationToken = default);

    Task<CampusSummary?> GetCampusAsync(Guid campusId, CancellationToken cancellationToken = default);

    /// <summary>Every scope in the tree (organisation, campuses, ministries), ordered by path.</summary>
    Task<IReadOnlyList<ScopeSummary>> GetScopesAsync(CancellationToken cancellationToken = default);

    Task<bool> ScopeExistsAsync(string path, CancellationToken cancellationToken = default);
}
