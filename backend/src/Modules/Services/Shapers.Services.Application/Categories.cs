using Microsoft.EntityFrameworkCore;
using Shapers.Church.Contracts;
using Shapers.Platform.Auditing;
using Shapers.Platform.Authorization;
using Shapers.Services.Contracts;
using Shapers.Services.Domain;

namespace Shapers.Services.Application;

/// <summary>
/// The categories teams are grouped in. Anyone who works with teams can read them; only people with
/// services.categories.manage change them.
/// </summary>
public sealed class CategoryService(IServicesDb db, IChurchDirectory church, IAuthorizer authorizer, IAuditLog audit)
{
    private static readonly Error NotFound = Error.NotFound("services.category_not_found", "Category not found.");

    /// <summary>The categories every church starts with; it can rename them, remove them and add its own.</summary>
    public static readonly (string Name, string Description)[] Typical =
    [
        ("Ministries", "People-focused groups: kids, youth, young adults, men, women"),
        ("Disciplines", "Serving departments: worship, production and media, hospitality, prayer"),
        ("Departments", "Running the church: administration, finance, facilities"),
        ("Spiritual growth", "Growth Track, discipleship and Bible study"),
    ];

    public async Task<IReadOnlyList<CategoryDto>> ListAsync(CancellationToken cancellationToken) =>
        await db.Categories.AsNoTracking().Where(c => !c.IsArchived).OrderBy(c => c.Order).ThenBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, c.Name, c.Description, c.Order)).ToListAsync(cancellationToken);

    public async Task<Result<CategoryDto>> CreateAsync(SaveCategoryRequest request, CancellationToken cancellationToken)
    {
        var root = ScopePath.Parse((await church.GetRootScopeAsync(cancellationToken)).Path);
        if (!await authorizer.CanAsync(ServicesPermissions.CategoriesManage, root, cancellationToken))
        {
            return Error.Forbidden("services.forbidden", "You can't manage categories.");
        }

        if (await db.Categories.AnyAsync(c => c.Name == request.Name.Trim() && !c.IsArchived, cancellationToken))
        {
            return Error.Conflict("services.category_exists", "There's already a category with that name.");
        }

        var category = TeamCategory.Create(request.Name, request.Description, request.Order, root);
        db.Categories.Add(category);
        await db.SaveChangesAsync(cancellationToken);
        await AuditAsync("services.category.created", category, cancellationToken);
        return ToDto(category);
    }

    public async Task<Result<CategoryDto>> UpdateAsync(Guid id, SaveCategoryRequest request, CancellationToken cancellationToken)
    {
        var category = await db.Categories.SingleOrDefaultAsync(c => c.Id == id && !c.IsArchived, cancellationToken);
        if (category is null || !await authorizer.CanAsync(ServicesPermissions.CategoriesManage, ScopePath.Parse(category.Scope), cancellationToken))
        {
            return NotFound;
        }

        category.Update(request.Name, request.Description, request.Order);
        await db.SaveChangesAsync(cancellationToken);
        await AuditAsync("services.category.updated", category, cancellationToken);
        return ToDto(category);
    }

    /// <summary>Removes a category. Its teams stay, uncategorised.</summary>
    public async Task<Result> ArchiveAsync(Guid id, CancellationToken cancellationToken)
    {
        var category = await db.Categories.SingleOrDefaultAsync(c => c.Id == id && !c.IsArchived, cancellationToken);
        if (category is null || !await authorizer.CanAsync(ServicesPermissions.CategoriesManage, ScopePath.Parse(category.Scope), cancellationToken))
        {
            return NotFound;
        }

        category.Archive();
        foreach (var team in await db.Teams.Where(t => t.CategoryId == id).ToListAsync(cancellationToken))
        {
            team.Update(team.Name, team.Description, team.OpenToMinors, null);
        }

        await db.SaveChangesAsync(cancellationToken);
        await AuditAsync("services.category.removed", category, cancellationToken);
        return Result.Success();
    }

    /// <summary>On first start, the typical categories, so a new church isn't starting from nothing.</summary>
    public async Task EnsureTypicalAsync(CancellationToken cancellationToken)
    {
        if (await db.Categories.AnyAsync(cancellationToken))
        {
            return;
        }

        var root = ScopePath.Parse((await church.GetRootScopeAsync(cancellationToken)).Path);
        for (var i = 0; i < Typical.Length; i++)
        {
            db.Categories.Add(TeamCategory.Create(Typical[i].Name, Typical[i].Description, i + 1, root));
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static CategoryDto ToDto(TeamCategory c) => new(c.Id, c.Name, c.Description, c.Order);

    private Task AuditAsync(string action, TeamCategory category, CancellationToken cancellationToken) =>
        audit.RecordAsync(new AuditRecord(action, "team_category", category.Id.ToString(), ScopePath.Parse(category.Scope), new { category.Name }), cancellationToken);
}
