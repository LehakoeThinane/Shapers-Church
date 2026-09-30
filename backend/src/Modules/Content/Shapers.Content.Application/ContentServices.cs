using Microsoft.EntityFrameworkCore;
using Shapers.Church.Contracts;
using Shapers.Content.Contracts;
using Shapers.Content.Domain;
using Shapers.Platform.Auditing;
using Shapers.Platform.Authorization;

namespace Shapers.Content.Application;

public interface IContentDb
{
    DbSet<Page> Pages { get; }

    DbSet<Post> Posts { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public sealed class ContentPermissionProvider : IPermissionProvider
{
    public IEnumerable<PermissionDefinition> GetPermissions() =>
    [
        new(ContentPermissions.Edit, "content", "Write pages, news and blog posts"),
        new(ContentPermissions.Publish, "content", "Publish, schedule and take down pages and posts"),
    ];
}

public sealed record SavePageRequest(string Title, string? Slug, string? Summary, string Body, int? MenuOrder);

public sealed record SavePostRequest(PostKind Kind, string Title, string? Slug, string? Summary, string Body, string? Author, string? CoverImageUrl, DateOnly? ShowUntil);

public sealed record ScheduleRequest(DateTimeOffset PublishAt);

public sealed record PageDto(Guid Id, string Title, string Slug, string? Summary, string Body, int? MenuOrder, DateTimeOffset UpdatedAt);

public sealed record PageAdminDto(PageDto Page, ContentStatus Status, DateTimeOffset? PublishAt, DateTimeOffset? PublishedAt, string? LegacyPath);

public sealed record PostSummaryDto(Guid Id, PostKind Kind, string Title, string Slug, string? Summary, string? Author, string? CoverImageUrl, DateTimeOffset PublishedAt);

public sealed record PostDto(PostSummaryDto Post, string Body);

public sealed record PostAdminDto(
    Guid Id,
    PostKind Kind,
    string Title,
    string Slug,
    string? Summary,
    string Body,
    string? Author,
    string? CoverImageUrl,
    DateOnly? ShowUntil,
    ContentStatus Status,
    DateTimeOffset? PublishAt,
    DateTimeOffset? PublishedAt,
    DateTimeOffset UpdatedAt,
    string? LegacyPath);

public sealed record MenuItemDto(string Title, string Slug);

public sealed record PostPageDto(IReadOnlyList<PostSummaryDto> Items, int Total, int Page, int PageSize);

/// <summary>An old WordPress address and where it lives now, for the website's redirects.</summary>
public sealed record RedirectDto(string From, string To);

/// <summary>Staff: pages and posts. Writing needs content.edit; going live or coming down needs content.publish.</summary>
public sealed class ContentAdminService(IContentDb db, IChurchDirectory church, IAuthorizer authorizer, IAuditLog audit, TimeProvider clock)
{
    private static readonly Error PageNotFound = Error.NotFound("content.page_not_found", "Page not found.");
    private static readonly Error PostNotFound = Error.NotFound("content.post_not_found", "Post not found.");

    // ---------- Pages ----------

    public async Task<IReadOnlyList<PageAdminDto>> PagesAsync(CancellationToken cancellationToken) =>
        (await db.Pages.AsNoTracking().OrderBy(p => p.MenuOrder == null).ThenBy(p => p.MenuOrder).ThenBy(p => p.Title).ToListAsync(cancellationToken))
            .Select(ToAdmin).ToList();

    public async Task<Result<PageAdminDto>> PageAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Pages.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken) is { } page ? ToAdmin(page) : PageNotFound;

    public async Task<Result<PageAdminDto>> CreatePageAsync(SavePageRequest request, CancellationToken cancellationToken)
    {
        var root = await RootAsync(cancellationToken);
        if (!await authorizer.CanAsync(ContentPermissions.Edit, root, cancellationToken))
        {
            return Error.Forbidden("content.forbidden", "You can't write pages.");
        }

        var now = clock.GetUtcNow();
        var page = Page.Create(request.Title, request.Summary, request.Body, root, now);
        page.Edit(request.Title, request.Summary, request.Body, request.MenuOrder, now);
        var slug = await ChooseSlugAsync(request.Slug ?? page.Slug, s => db.Pages.AnyAsync(p => p.Slug == s, cancellationToken));
        if (slug.IsFailure)
        {
            return slug.Error!;
        }

        page.UseSlug(slug.Value);
        db.Pages.Add(page);
        await db.SaveChangesAsync(cancellationToken);
        await AuditAsync("content.page.created", page, cancellationToken);
        return ToAdmin(page);
    }

    public async Task<Result<PageAdminDto>> UpdatePageAsync(Guid id, SavePageRequest request, CancellationToken cancellationToken)
    {
        var page = await db.Pages.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (page is null || !await authorizer.CanAsync(ContentPermissions.Edit, ScopePath.Parse(page.Scope), cancellationToken))
        {
            return PageNotFound;
        }

        if (request.Slug is { } wanted && wanted != page.Slug)
        {
            if (await db.Pages.AnyAsync(p => p.Slug == wanted && p.Id != id, cancellationToken))
            {
                return Error.Conflict("content.slug_taken", "Another page already uses that address.");
            }

            page.UseSlug(wanted);
        }

        page.Edit(request.Title, request.Summary, request.Body, request.MenuOrder, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await AuditAsync("content.page.edited", page, cancellationToken);
        return ToAdmin(page);
    }

    public Task<Result<PageAdminDto>> ChangePageAsync(Guid id, string action, Action<Page, DateTimeOffset> change, CancellationToken cancellationToken) =>
        ChangeAsync(db.Pages, id, action, change, ToAdmin, PageNotFound, cancellationToken);

    // ---------- Posts ----------

    public async Task<IReadOnlyList<PostAdminDto>> PostsAsync(PostKind? kind, CancellationToken cancellationToken)
    {
        var query = db.Posts.AsNoTracking();
        if (kind is { } k)
        {
            query = query.Where(p => p.Kind == k);
        }

        return (await query.OrderByDescending(p => p.UpdatedAt).Take(200).ToListAsync(cancellationToken)).Select(ToAdmin).ToList();
    }

    public async Task<Result<PostAdminDto>> PostAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Posts.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken) is { } post ? ToAdmin(post) : PostNotFound;

    public async Task<Result<PostAdminDto>> CreatePostAsync(SavePostRequest request, CancellationToken cancellationToken)
    {
        var root = await RootAsync(cancellationToken);
        if (!await authorizer.CanAsync(ContentPermissions.Edit, root, cancellationToken))
        {
            return Error.Forbidden("content.forbidden", "You can't write posts.");
        }

        var now = clock.GetUtcNow();
        var post = Post.Create(request.Kind, request.Title, request.Summary, request.Body, root, now);
        post.Edit(request.Kind, request.Title, request.Summary, request.Body, request.Author, request.CoverImageUrl, request.ShowUntil, now);
        var slug = await ChooseSlugAsync(request.Slug ?? post.Slug, s => db.Posts.AnyAsync(p => p.Slug == s, cancellationToken));
        if (slug.IsFailure)
        {
            return slug.Error!;
        }

        post.UseSlug(slug.Value);
        db.Posts.Add(post);
        await db.SaveChangesAsync(cancellationToken);
        await AuditAsync("content.post.created", post, cancellationToken);
        return ToAdmin(post);
    }

    public async Task<Result<PostAdminDto>> UpdatePostAsync(Guid id, SavePostRequest request, CancellationToken cancellationToken)
    {
        var post = await db.Posts.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (post is null || !await authorizer.CanAsync(ContentPermissions.Edit, ScopePath.Parse(post.Scope), cancellationToken))
        {
            return PostNotFound;
        }

        if (request.Slug is { } wanted && wanted != post.Slug)
        {
            if (await db.Posts.AnyAsync(p => p.Slug == wanted && p.Id != id, cancellationToken))
            {
                return Error.Conflict("content.slug_taken", "Another post already uses that address.");
            }

            post.UseSlug(wanted);
        }

        post.Edit(request.Kind, request.Title, request.Summary, request.Body, request.Author, request.CoverImageUrl, request.ShowUntil, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await AuditAsync("content.post.edited", post, cancellationToken);
        return ToAdmin(post);
    }

    public Task<Result<PostAdminDto>> ChangePostAsync(Guid id, string action, Action<Post, DateTimeOffset> change, CancellationToken cancellationToken) =>
        ChangeAsync(db.Posts, id, action, change, ToAdmin, PostNotFound, cancellationToken);

    // ---------- Shared ----------

    private async Task<Result<TDto>> ChangeAsync<TItem, TDto>(
        DbSet<TItem> set, Guid id, string action, Action<TItem, DateTimeOffset> change, Func<TItem, TDto> toDto, Error notFound, CancellationToken cancellationToken)
        where TItem : PublishableContent
    {
        var item = await set.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null || !await authorizer.CanAsync(ContentPermissions.Publish, ScopePath.Parse(item.Scope), cancellationToken))
        {
            return notFound;
        }

        change(item, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await AuditAsync(action, item, cancellationToken);
        return toDto(item);
    }

    private static async Task<Result<string>> ChooseSlugAsync(string wanted, Func<string, Task<bool>> taken)
    {
        if (!SlugRules.IsValid(wanted))
        {
            return new Error("content.slug_invalid", "Use lower-case letters, numbers and dashes, e.g. about-us.");
        }

        var slug = wanted;
        for (var n = 2; await taken(slug); n++)
        {
            slug = $"{wanted}-{n}";
        }

        return slug;
    }

    private async Task<ScopePath> RootAsync(CancellationToken cancellationToken) => ScopePath.Parse((await church.GetRootScopeAsync(cancellationToken)).Path);

    private Task AuditAsync(string action, PublishableContent item, CancellationToken cancellationToken) =>
        audit.RecordAsync(new AuditRecord(action, item.GetType().Name.ToLowerInvariant(), item.Id.ToString(), ScopePath.Parse(item.Scope), new { item.Title, status = item.Status.ToString() }), cancellationToken);

    private static PageAdminDto ToAdmin(Page p) =>
        new(new PageDto(p.Id, p.Title, p.Slug, p.Summary, p.Body, p.MenuOrder, p.UpdatedAt), p.Status, p.PublishAt, p.PublishedAt, p.LegacyPath);

    private static PostAdminDto ToAdmin(Post p) =>
        new(p.Id, p.Kind, p.Title, p.Slug, p.Summary, p.Body, p.Author, p.CoverImageUrl, p.ShowUntil, p.Status, p.PublishAt, p.PublishedAt, p.UpdatedAt, p.LegacyPath);
}

/// <summary>What the app and the website read. Only live content; anyone may read it.</summary>
public sealed class PublicContentService(IContentDb db, TimeProvider clock)
{
    private static readonly Error NotFound = Error.NotFound("content.not_found", "Not found.");

    public async Task<IReadOnlyList<MenuItemDto>> MenuAsync(CancellationToken cancellationToken) =>
        await db.Pages.AsNoTracking()
            .Where(p => p.Status == ContentStatus.Published && p.MenuOrder != null)
            .OrderBy(p => p.MenuOrder)
            .Select(p => new MenuItemDto(p.Title, p.Slug))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PageDto>> PagesAsync(CancellationToken cancellationToken) =>
        await db.Pages.AsNoTracking()
            .Where(p => p.Status == ContentStatus.Published)
            .OrderBy(p => p.Title)
            .Select(p => new PageDto(p.Id, p.Title, p.Slug, p.Summary, p.Body, p.MenuOrder, p.UpdatedAt))
            .ToListAsync(cancellationToken);

    public async Task<Result<PageDto>> PageAsync(string slug, CancellationToken cancellationToken) =>
        await db.Pages.AsNoTracking()
            .Where(p => p.Slug == slug && p.Status == ContentStatus.Published)
            .Select(p => new PageDto(p.Id, p.Title, p.Slug, p.Summary, p.Body, p.MenuOrder, p.UpdatedAt))
            .SingleOrDefaultAsync(cancellationToken) is { } page ? page : NotFound;

    public async Task<PostPageDto> PostsAsync(PostKind? kind, int page, int pageSize, CancellationToken cancellationToken)
    {
        pageSize = Math.Clamp(pageSize, 1, 50);
        page = Math.Max(1, page);
        var query = db.Posts.AsNoTracking().Where(p => p.Status == ContentStatus.Published);
        if (kind is { } k)
        {
            query = query.Where(p => p.Kind == k);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(p => p.PublishedAt).Skip((page - 1) * pageSize).Take(pageSize).Select(Summary).ToListAsync(cancellationToken);
        return new PostPageDto(items, total, page, pageSize);
    }

    /// <summary>News still in date, newest first: for the app's Home and the website's front page.</summary>
    public async Task<IReadOnlyList<PostSummaryDto>> CurrentNewsAsync(int count, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        return await db.Posts.AsNoTracking()
            .Where(p => p.Kind == PostKind.News && p.Status == ContentStatus.Published && (p.ShowUntil == null || p.ShowUntil >= today))
            .OrderByDescending(p => p.PublishedAt)
            .Take(Math.Clamp(count, 1, 10))
            .Select(Summary)
            .ToListAsync(cancellationToken);
    }

    public async Task<Result<PostDto>> PostAsync(string slug, CancellationToken cancellationToken) =>
        await db.Posts.AsNoTracking()
            .Where(p => p.Slug == slug && p.Status == ContentStatus.Published)
            .Select(p => new PostDto(new PostSummaryDto(p.Id, p.Kind, p.Title, p.Slug, p.Summary, p.Author, p.CoverImageUrl, p.PublishedAt!.Value), p.Body))
            .SingleOrDefaultAsync(cancellationToken) is { } post ? post : NotFound;

    /// <summary>Old WordPress addresses of everything imported, and where each now lives.</summary>
    public async Task<IReadOnlyList<RedirectDto>> RedirectsAsync(CancellationToken cancellationToken)
    {
        var pages = await db.Pages.AsNoTracking().Where(p => p.LegacyPath != null && p.Status == ContentStatus.Published)
            .Select(p => new RedirectDto(p.LegacyPath!, "/" + p.Slug)).ToListAsync(cancellationToken);
        var posts = await db.Posts.AsNoTracking().Where(p => p.LegacyPath != null && p.Status == ContentStatus.Published)
            .Select(p => new RedirectDto(p.LegacyPath!, "/blog/" + p.Slug)).ToListAsync(cancellationToken);
        return [.. pages, .. posts];
    }

    private static readonly System.Linq.Expressions.Expression<Func<Post, PostSummaryDto>> Summary =
        p => new PostSummaryDto(p.Id, p.Kind, p.Title, p.Slug, p.Summary, p.Author, p.CoverImageUrl, p.PublishedAt!.Value);
}

/// <summary>Every minute: scheduled pages and posts whose time has come go live.</summary>
public sealed class ContentPublisherJob(IContentDb db, TimeProvider clock)
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var pages = await db.Pages.Where(p => p.Status == ContentStatus.Scheduled && p.PublishAt <= now).ToListAsync(cancellationToken);
        var posts = await db.Posts.Where(p => p.Status == ContentStatus.Scheduled && p.PublishAt <= now).ToListAsync(cancellationToken);
        var published = pages.Count(p => p.PublishIfDue(now)) + posts.Count(p => p.PublishIfDue(now));
        await db.SaveChangesAsync(cancellationToken);
        return published;
    }
}
