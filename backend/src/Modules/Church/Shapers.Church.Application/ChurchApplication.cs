using Microsoft.EntityFrameworkCore;
using Shapers.Church.Contracts;
using Shapers.Church.Domain;
using Shapers.Platform.Authorization;
using Shapers.SharedKernel;

namespace Shapers.Church.Application;

public interface IChurchDb
{
    DbSet<Organisation> Organisations { get; }

    DbSet<Campus> Campuses { get; }

    DbSet<Ministry> Ministries { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public sealed class ChurchPermissionProvider : IPermissionProvider
{
    public IEnumerable<PermissionDefinition> GetPermissions() =>
    [
        new(ChurchPermissions.OrganisationManage, "church", "Edit church details, tax status and settings"),
        new(ChurchPermissions.CampusesManage, "church", "Create and edit campuses"),
        new(ChurchPermissions.MinistriesManage, "church", "Create and edit ministries"),
    ];
}

public sealed record AddressDto(string Line1, string? Line2, string? Suburb, string City, string Province, string PostalCode, string CountryCode = "ZA")
{
    public Address ToDomain() => new(Line1, Line2, Suburb, City, Province, PostalCode, CountryCode);

    public static AddressDto? From(Address? a) =>
        a is null ? null : new(a.Line1, a.Line2, a.Suburb, a.City, a.Province, a.PostalCode, a.CountryCode);
}

public sealed record OrganisationDto(Guid Id, string Name, string? LegalName, string Scope, string TimeZone, string Currency, string? ContactEmail, string? Website, bool IsSection18AApproved);

public sealed record CampusDto(Guid Id, string Name, string Slug, string Scope, AddressDto? Address, string Status, bool IsPrimary);

public sealed record MinistryDto(Guid Id, string Name, string Slug, string Scope, Guid? CampusId, bool IsActive);

public sealed record ChurchOverviewDto(OrganisationDto Organisation, IReadOnlyList<CampusDto> Campuses);

public sealed record CreateCampusRequest(string Name, string Slug, AddressDto? Address);

public sealed record UpdateCampusRequest(string Name, AddressDto? Address, CampusStatus Status);

public sealed record CreateMinistryRequest(string Name, string Slug, Guid? CampusId);

public sealed class ChurchService(IChurchDb db, IAuthorizer authorizer)
{
    public async Task<ChurchOverviewDto> GetOverviewAsync(CancellationToken cancellationToken)
    {
        var organisation = await db.Organisations.AsNoTracking().SingleAsync(cancellationToken);
        var campuses = await db.Campuses.AsNoTracking()
            .Where(c => c.Status != CampusStatus.Closed)
            .OrderByDescending(c => c.IsPrimary).ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);
        return new ChurchOverviewDto(ToDto(organisation), campuses.Select(ToDto).ToList());
    }

    public async Task<IReadOnlyList<CampusDto>> ListCampusesAsync(CancellationToken cancellationToken)
    {
        var campuses = await db.Campuses.AsNoTracking().OrderBy(c => c.Name).ToListAsync(cancellationToken);
        return campuses.Select(ToDto).ToList();
    }

    public async Task<Result<CampusDto>> CreateCampusAsync(CreateCampusRequest request, CancellationToken cancellationToken)
    {
        var organisation = await db.Organisations.SingleAsync(cancellationToken);
        if (!await authorizer.CanAsync(ChurchPermissions.CampusesManage, ScopePath.Parse(organisation.Scope), cancellationToken))
        {
            return Error.Forbidden("church.forbidden", "Only church-wide administrators can add campuses.");
        }

        var slug = ScopePath.Label(request.Slug);
        if (await db.Campuses.AnyAsync(c => c.Slug == slug, cancellationToken))
        {
            return Error.Conflict("church.campus_slug_taken", $"A campus with the short name '{slug}' already exists.");
        }

        var isFirst = !await db.Campuses.AnyAsync(cancellationToken);
        var campus = Campus.Create(organisation, request.Name, slug, request.Address?.ToDomain(), isPrimary: isFirst);
        db.Campuses.Add(campus);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(campus);
    }

    public async Task<Result<CampusDto>> UpdateCampusAsync(Guid id, UpdateCampusRequest request, CancellationToken cancellationToken)
    {
        var campus = await db.Campuses.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (campus is null)
        {
            return Error.NotFound("church.campus_not_found", "Campus not found.");
        }

        if (!await authorizer.CanAsync(ChurchPermissions.CampusesManage, ScopePath.Parse(campus.Scope), cancellationToken))
        {
            return Error.Forbidden("church.forbidden", "You cannot edit this campus.");
        }

        campus.Update(request.Name, request.Address?.ToDomain(), request.Status);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(campus);
    }

    public async Task<IReadOnlyList<MinistryDto>> ListMinistriesAsync(CancellationToken cancellationToken)
    {
        var ministries = await db.Ministries.AsNoTracking().OrderBy(m => m.Scope).ToListAsync(cancellationToken);
        return ministries.Select(ToDto).ToList();
    }

    public async Task<Result<MinistryDto>> CreateMinistryAsync(CreateMinistryRequest request, CancellationToken cancellationToken)
    {
        var organisation = await db.Organisations.SingleAsync(cancellationToken);
        Campus? campus = null;
        if (request.CampusId is { } campusId)
        {
            campus = await db.Campuses.SingleOrDefaultAsync(c => c.Id == campusId, cancellationToken);
            if (campus is null)
            {
                return Error.NotFound("church.campus_not_found", "Campus not found.");
            }
        }

        var parent = ScopePath.Parse(campus?.Scope ?? organisation.Scope);
        if (!await authorizer.CanAsync(ChurchPermissions.MinistriesManage, parent, cancellationToken))
        {
            return Error.Forbidden("church.forbidden", "You cannot add ministries here.");
        }

        var scope = parent.Child(ScopeType.Ministry, request.Slug).Value;
        if (await db.Ministries.AnyAsync(m => m.Scope == scope, cancellationToken))
        {
            return Error.Conflict("church.ministry_exists", "A ministry with that short name already exists here.");
        }

        var ministry = Ministry.Create(organisation, campus, request.Name, request.Slug);
        db.Ministries.Add(ministry);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(ministry);
    }

    private static OrganisationDto ToDto(Organisation o) =>
        new(o.Id, o.Name, o.LegalName, o.Scope, o.TimeZone, o.Currency, o.ContactEmail, o.Website, o.IsSection18AApproved);

    private static CampusDto ToDto(Campus c) =>
        new(c.Id, c.Name, c.Slug, c.Scope, AddressDto.From(c.Address), c.Status.ToString(), c.IsPrimary);

    private static MinistryDto ToDto(Ministry m) => new(m.Id, m.Name, m.Slug, m.Scope, m.CampusId, m.IsActive);
}
