using Microsoft.EntityFrameworkCore;
using Shapers.Church.Contracts;
using Shapers.Church.Domain;

namespace Shapers.Church.Infrastructure;

internal sealed class ChurchDirectory(ChurchDbContext db) : IChurchDirectory
{
    public async Task<ScopeSummary> GetRootScopeAsync(CancellationToken cancellationToken = default)
    {
        var organisation = await db.Organisations.AsNoTracking().SingleAsync(cancellationToken);
        return new ScopeSummary(organisation.Scope, organisation.Name, "Global", organisation.Id);
    }

    public async Task<IReadOnlyList<CampusSummary>> GetCampusesAsync(CancellationToken cancellationToken = default) =>
        await db.Campuses.AsNoTracking()
            .OrderByDescending(c => c.IsPrimary).ThenBy(c => c.Name)
            .Select(c => new CampusSummary(c.Id, c.Name, c.Scope, c.IsPrimary, c.Status.ToString()))
            .ToListAsync(cancellationToken);

    public Task<CampusSummary?> GetCampusAsync(Guid campusId, CancellationToken cancellationToken = default) =>
        db.Campuses.AsNoTracking()
            .Where(c => c.Id == campusId)
            .Select(c => new CampusSummary(c.Id, c.Name, c.Scope, c.IsPrimary, c.Status.ToString()))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<ScopeSummary>> GetScopesAsync(CancellationToken cancellationToken = default)
    {
        var root = await GetRootScopeAsync(cancellationToken);
        var campuses = await db.Campuses.AsNoTracking()
            .Where(c => c.Status != CampusStatus.Closed)
            .Select(c => new ScopeSummary(c.Scope, c.Name, "Campus", c.Id))
            .ToListAsync(cancellationToken);
        var ministries = await db.Ministries.AsNoTracking()
            .Where(m => m.IsActive)
            .Select(m => new ScopeSummary(m.Scope, m.Name, "Ministry", m.Id))
            .ToListAsync(cancellationToken);

        return [root, .. campuses.Concat(ministries).OrderBy(s => s.Path, StringComparer.Ordinal)];
    }

    public async Task<bool> ScopeExistsAsync(string path, CancellationToken cancellationToken = default) =>
        await db.Organisations.AnyAsync(o => o.Scope == path, cancellationToken)
        || await db.Campuses.AnyAsync(c => c.Scope == path, cancellationToken)
        || await db.Ministries.AnyAsync(m => m.Scope == path, cancellationToken);
}
