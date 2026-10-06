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
        new(ContentPermissions.TranslationsReview, "content", "Check translations of pages and posts before they're published"),
    ];
}

public sealed record SavePageRequest(string Title, string? Slug, string? Summary, string Body, int? MenuOrder);

public sealed record SavePostRequest(PostKind Kind, string Title, string? Slug, string? Summary, string Body, string? Author, string? CoverImageUrl, DateOnly? ShowUntil);

public sealed record ScheduleRequest(DateTimeOffset PublishAt);

public sealed record LanguageDto(string Code, string Name);

/// <param name="Languages">Every language this page can be read in (English and checked, published translations).</param>
public sealed record PageDto(Guid Id, string Title, string Slug, string? Summary, string Body, int? MenuOrder, DateTimeOffset UpdatedAt, string Language, IReadOnlyList<LanguageDto> Languages);

/// <summary>Where a translation stands, for the original's editor and the translations list.</summary>
public sealed record TranslationSummaryDto(Guid Id, string Language, string LanguageName, ContentStatus Status, bool Checked, bool OriginalChangedSince);

/// <summary>Translation details when this item is a translation; null for originals.</summary>
public sealed record TranslationInfoDto(Guid OriginalId, string OriginalTitle, string Language, string LanguageName, bool Checked, DateTimeOffset? CheckedAt, bool OriginalChangedSince);

public sealed record PageAdminDto(
    PageDto Page,
    ContentStatus Status,
    DateTimeOffset? PublishAt,
    DateTimeOffset? PublishedAt,
    string? LegacyPath,
    TranslationInfoDto? Translation,
    IReadOnlyList<TranslationSummaryDto> Translations);

public sealed record PostSummaryDto(Guid Id, PostKind Kind, string Title, string Slug, string? Summary, string? Author, string? CoverImageUrl, DateTimeOffset PublishedAt);

/// <param name="Languages">Every language this post can be read in (English and checked, published translations).</param>
public sealed record PostDto(PostSummaryDto Post, string Body, string Language, IReadOnlyList<LanguageDto> Languages);

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
    string? LegacyPath,
    TranslationInfoDto? Translation,
    IReadOnlyList<TranslationSummaryDto> Translations);

/// <summary>The words of a new translation, usually an AI draft a speaker will check.</summary>
public sealed record CreateTranslationRequest(string Language, string Title, string? Summary, string Body);

/// <summary>A translation in the translators' list.</summary>
public sealed record TranslationQueueItemDto(
    Guid Id,
    ContentType Type,
    string Language,
    string LanguageName,
    string Title,
    string OriginalTitle,
    ContentStatus Status,
    bool Checked,
    bool OriginalChangedSince,
    DateTimeOffset UpdatedAt);

public sealed record MenuItemDto(string Title, string Slug);

public sealed record PostPageDto(IReadOnlyList<PostSummaryDto> Items, int Total, int Page, int PageSize);

/// <summary>An old WordPress address and where it lives now, for the website's redirects.</summary>
public sealed record RedirectDto(string From, string To);

/// <summary>
/// Staff: pages and posts. Writing needs content.edit; going live or coming down needs content.publish. Translations
/// can also be edited by translators (content.translations.review), who alone can mark them checked.
/// </summary>
public sealed class ContentAdminService(IContentDb db, IChurchDirectory church, IAuthorizer authorizer, ICurrentUser currentUser, IAuditLog audit, TimeProvider clock)
{
    private static readonly Error PageNotFound = Error.NotFound("content.page_not_found", "Page not found.");
    private static readonly Error PostNotFound = Error.NotFound("content.post_not_found", "Post not found.");

    // ---------- Pages ----------

    public async Task<IReadOnlyList<PageAdminDto>> PagesAsync(CancellationToken cancellationToken)
    {
        var all = await db.Pages.AsNoTracking().ToListAsync(cancellationToken);
        return all.Where(p => !p.IsTranslation)
            .OrderBy(p => p.MenuOrder == null).ThenBy(p => p.MenuOrder).ThenBy(p => p.Title)
            .Select(p => ToAdmin(p, null, all.Where(t => t.TranslationOfId == p.Id)))
            .ToList();
    }

    public async Task<Result<PageAdminDto>> PageAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Pages.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken) is { } page ? await PageDtoAsync(page, cancellationToken) : PageNotFound;

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
        var slug = await ChooseSlugAsync(request.Slug ?? page.Slug, s => db.Pages.AnyAsync(p => p.Slug == s && p.TranslationOfId == null, cancellationToken));
        if (slug.IsFailure)
        {
            return slug.Error!;
        }

        page.UseSlug(slug.Value);
        db.Pages.Add(page);
        await db.SaveChangesAsync(cancellationToken);
        await AuditAsync("content.page.created", page, cancellationToken);
        return await PageDtoAsync(page, cancellationToken);
    }

    public async Task<Result<PageAdminDto>> UpdatePageAsync(Guid id, SavePageRequest request, CancellationToken cancellationToken)
    {
        var page = await db.Pages.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (page is null || !await CanEditAsync(page, cancellationToken))
        {
            return PageNotFound;
        }

        // A translation's address follows its original.
        if (!page.IsTranslation && request.Slug is { } wanted && wanted != page.Slug)
        {
            if (await db.Pages.AnyAsync(p => p.Slug == wanted && p.TranslationOfId == null && p.Id != id, cancellationToken))
            {
                return Error.Conflict("content.slug_taken", "Another page already uses that address.");
            }

            page.UseSlug(wanted);
            foreach (var translation in await db.Pages.Where(t => t.TranslationOfId == id).ToListAsync(cancellationToken))
            {
                translation.UseSlug(wanted);
            }
        }

        page.Edit(request.Title, request.Summary, request.Body, request.MenuOrder, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await AuditAsync("content.page.edited", page, cancellationToken);
        return await PageDtoAsync(page, cancellationToken);
    }

    public async Task<Result<PageAdminDto>> TranslatePageAsync(Guid id, CreateTranslationRequest request, CancellationToken cancellationToken)
    {
        var original = await db.Pages.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (original is null || !await authorizer.CanAsync(ContentPermissions.Edit, ScopePath.Parse(original.Scope), cancellationToken))
        {
            return PageNotFound;
        }

        if (await db.Pages.AnyAsync(p => p.TranslationOfId == id && p.Language == request.Language, cancellationToken))
        {
            return Error.Conflict("content.translation_exists", $"There's already a {Languages.NameOf(request.Language)} translation. Open it to carry on.");
        }

        var page = Page.TranslationOf(original, request.Language, request.Title, request.Summary, request.Body, clock.GetUtcNow());
        db.Pages.Add(page);
        await db.SaveChangesAsync(cancellationToken);
        await AuditAsync("content.page.translated", page, cancellationToken);
        return await PageDtoAsync(page, cancellationToken);
    }

    public Task<Result<PageAdminDto>> CheckPageAsync(Guid id, CancellationToken cancellationToken) =>
        CheckAsync(db.Pages, id, PageNotFound, PageDtoAsync, cancellationToken);

    public Task<Result<PageAdminDto>> ChangePageAsync(Guid id, string action, Action<Page, DateTimeOffset> change, CancellationToken cancellationToken) =>
        ChangeAsync(db.Pages, id, action, change, PageDtoAsync, PageNotFound, cancellationToken);

    // ---------- Posts ----------

    public async Task<IReadOnlyList<PostAdminDto>> PostsAsync(PostKind? kind, CancellationToken cancellationToken)
    {
        var query = db.Posts.AsNoTracking().Where(p => p.TranslationOfId == null);
        if (kind is { } k)
        {
            query = query.Where(p => p.Kind == k);
        }

        var posts = await query.OrderByDescending(p => p.UpdatedAt).Take(200).ToListAsync(cancellationToken);
        var ids = posts.Select(p => p.Id).ToList();
        var translations = await db.Posts.AsNoTracking().Where(t => t.TranslationOfId != null && ids.Contains(t.TranslationOfId.Value)).ToListAsync(cancellationToken);
        return posts.Select(p => ToAdmin(p, null, translations.Where(t => t.TranslationOfId == p.Id))).ToList();
    }

    public async Task<Result<PostAdminDto>> PostAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Posts.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken) is { } post ? await PostDtoAsync(post, cancellationToken) : PostNotFound;

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
        var slug = await ChooseSlugAsync(request.Slug ?? post.Slug, s => db.Posts.AnyAsync(p => p.Slug == s && p.TranslationOfId == null, cancellationToken));
        if (slug.IsFailure)
        {
            return slug.Error!;
        }

        post.UseSlug(slug.Value);
        db.Posts.Add(post);
        await db.SaveChangesAsync(cancellationToken);
        await AuditAsync("content.post.created", post, cancellationToken);
        return await PostDtoAsync(post, cancellationToken);
    }

    public async Task<Result<PostAdminDto>> UpdatePostAsync(Guid id, SavePostRequest request, CancellationToken cancellationToken)
    {
        var post = await db.Posts.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (post is null || !await CanEditAsync(post, cancellationToken))
        {
            return PostNotFound;
        }

        if (!post.IsTranslation && request.Slug is { } wanted && wanted != post.Slug)
        {
            if (await db.Posts.AnyAsync(p => p.Slug == wanted && p.TranslationOfId == null && p.Id != id, cancellationToken))
            {
                return Error.Conflict("content.slug_taken", "Another post already uses that address.");
            }

            post.UseSlug(wanted);
            foreach (var translation in await db.Posts.Where(t => t.TranslationOfId == id).ToListAsync(cancellationToken))
            {
                translation.UseSlug(wanted);
            }
        }

        post.Edit(request.Kind, request.Title, request.Summary, request.Body, request.Author, request.CoverImageUrl, request.ShowUntil, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await AuditAsync("content.post.edited", post, cancellationToken);
        return await PostDtoAsync(post, cancellationToken);
    }

    public async Task<Result<PostAdminDto>> TranslatePostAsync(Guid id, CreateTranslationRequest request, CancellationToken cancellationToken)
    {
        var original = await db.Posts.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (original is null || !await authorizer.CanAsync(ContentPermissions.Edit, ScopePath.Parse(original.Scope), cancellationToken))
        {
            return PostNotFound;
        }

        if (await db.Posts.AnyAsync(p => p.TranslationOfId == id && p.Language == request.Language, cancellationToken))
        {
            return Error.Conflict("content.translation_exists", $"There's already a {Languages.NameOf(request.Language)} translation. Open it to carry on.");
        }

        var post = Post.TranslationOf(original, request.Language, request.Title, request.Summary, request.Body, clock.GetUtcNow());
        db.Posts.Add(post);
        await db.SaveChangesAsync(cancellationToken);
        await AuditAsync("content.post.translated", post, cancellationToken);
        return await PostDtoAsync(post, cancellationToken);
    }

    public Task<Result<PostAdminDto>> CheckPostAsync(Guid id, CancellationToken cancellationToken) =>
        CheckAsync(db.Posts, id, PostNotFound, PostDtoAsync, cancellationToken);

    public Task<Result<PostAdminDto>> ChangePostAsync(Guid id, string action, Action<Post, DateTimeOffset> change, CancellationToken cancellationToken) =>
        ChangeAsync(db.Posts, id, action, change, PostDtoAsync, PostNotFound, cancellationToken);

    // ---------- Translations ----------

    /// <summary>Every translation, unchecked first: the translators' to-do list.</summary>
    public async Task<IReadOnlyList<TranslationQueueItemDto>> TranslationsAsync(CancellationToken cancellationToken)
    {
        var pages = await db.Pages.AsNoTracking().ToListAsync(cancellationToken);
        var posts = await db.Posts.AsNoTracking().Where(p => p.TranslationOfId != null).ToListAsync(cancellationToken);
        var postOriginalIds = posts.Select(p => p.TranslationOfId!.Value).Distinct().ToList();
        var postOriginals = await db.Posts.AsNoTracking().Where(p => postOriginalIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);
        var pageOriginals = pages.Where(p => !p.IsTranslation).ToDictionary(p => p.Id);

        var items = pages.Where(p => p.IsTranslation && pageOriginals.ContainsKey(p.TranslationOfId!.Value))
            .Select(p => Queued(p, ContentType.Page, pageOriginals[p.TranslationOfId!.Value]))
            .Concat(posts.Where(p => postOriginals.ContainsKey(p.TranslationOfId!.Value)).Select(p => Queued(p, ContentType.Post, postOriginals[p.TranslationOfId!.Value])));
        return items.OrderBy(i => i.Checked && !i.OriginalChangedSince).ThenByDescending(i => i.UpdatedAt).ToList();

        static TranslationQueueItemDto Queued(PublishableContent t, ContentType type, PublishableContent original) =>
            new(t.Id, type, t.Language, Languages.NameOf(t.Language), t.Title, original.Title, t.Status, t.TranslationChecked, ChangedSince(t, original), t.UpdatedAt);
    }

    // ---------- Shared ----------

    private async Task<Result<TDto>> ChangeAsync<TItem, TDto>(
        DbSet<TItem> set, Guid id, string action, Action<TItem, DateTimeOffset> change, Func<TItem, CancellationToken, Task<TDto>> toDto, Error notFound, CancellationToken cancellationToken)
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
        return await toDto(item, cancellationToken);
    }

    /// <summary>A speaker of the language confirms the translation says what the original says.</summary>
    private async Task<Result<TDto>> CheckAsync<TItem, TDto>(DbSet<TItem> set, Guid id, Error notFound, Func<TItem, CancellationToken, Task<TDto>> toDto, CancellationToken cancellationToken)
        where TItem : PublishableContent
    {
        var item = await set.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null || !await authorizer.CanAsync(ContentPermissions.TranslationsReview, ScopePath.Parse(item.Scope), cancellationToken))
        {
            return notFound;
        }

        if (!item.IsTranslation)
        {
            return new Error("content.not_translation", "Only translations are checked.");
        }

        var originalVersion = await set.AsNoTracking().Where(x => x.Id == item.TranslationOfId).Select(x => x.UpdatedAt).SingleAsync(cancellationToken);
        item.CheckTranslation(currentUser.UserId!.Value, originalVersion, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(
            new AuditRecord("content.translation.checked", item.GetType().Name.ToLowerInvariant(), item.Id.ToString(), ScopePath.Parse(item.Scope), new { item.Title, item.Language }),
            cancellationToken);
        return await toDto(item, cancellationToken);
    }

    private async Task<bool> CanEditAsync(PublishableContent item, CancellationToken cancellationToken)
    {
        var scope = ScopePath.Parse(item.Scope);
        return await authorizer.CanAsync(ContentPermissions.Edit, scope, cancellationToken)
            || (item.IsTranslation && await authorizer.CanAsync(ContentPermissions.TranslationsReview, scope, cancellationToken));
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
        audit.RecordAsync(new AuditRecord(action, item.GetType().Name.ToLowerInvariant(), item.Id.ToString(), ScopePath.Parse(item.Scope), new { item.Title, item.Language, status = item.Status.ToString() }), cancellationToken);

    private async Task<PageAdminDto> PageDtoAsync(Page page, CancellationToken cancellationToken)
    {
        var originalId = page.TranslationOfId ?? page.Id;
        var original = page.IsTranslation ? await db.Pages.AsNoTracking().SingleOrDefaultAsync(p => p.Id == originalId, cancellationToken) : null;
        var translations = page.IsTranslation ? [] : await db.Pages.AsNoTracking().Where(t => t.TranslationOfId == originalId).ToListAsync(cancellationToken);
        return ToAdmin(page, original, translations);
    }

    private async Task<PostAdminDto> PostDtoAsync(Post post, CancellationToken cancellationToken)
    {
        var originalId = post.TranslationOfId ?? post.Id;
        var original = post.IsTranslation ? await db.Posts.AsNoTracking().SingleOrDefaultAsync(p => p.Id == originalId, cancellationToken) : null;
        var translations = post.IsTranslation ? [] : await db.Posts.AsNoTracking().Where(t => t.TranslationOfId == originalId).ToListAsync(cancellationToken);
        return ToAdmin(post, original, translations);
    }

    private static bool ChangedSince(PublishableContent translation, PublishableContent original) =>
        translation.TranslatedFromVersion is { } version && original.UpdatedAt > version;

    private static TranslationInfoDto? Info(PublishableContent item, PublishableContent? original) =>
        item.IsTranslation && original is not null
            ? new TranslationInfoDto(original.Id, original.Title, item.Language, Languages.NameOf(item.Language), item.TranslationChecked, item.TranslationCheckedAt, ChangedSince(item, original))
            : null;

    private static List<TranslationSummaryDto> Summaries(PublishableContent original, IEnumerable<PublishableContent> translations) =>
        translations.OrderBy(t => t.Language)
            .Select(t => new TranslationSummaryDto(t.Id, t.Language, Languages.NameOf(t.Language), t.Status, t.TranslationChecked, ChangedSince(t, original)))
            .ToList();

    private static PageAdminDto ToAdmin(Page p, Page? original, IEnumerable<Page> translations) =>
        new(
            new PageDto(p.Id, p.Title, p.Slug, p.Summary, p.Body, p.MenuOrder, p.UpdatedAt, p.Language, []),
            p.Status,
            p.PublishAt,
            p.PublishedAt,
            p.LegacyPath,
            Info(p, original),
            Summaries(p, translations));

    private static PostAdminDto ToAdmin(Post p, Post? original, IEnumerable<Post> translations) =>
        new(p.Id, p.Kind, p.Title, p.Slug, p.Summary, p.Body, p.Author, p.CoverImageUrl, p.ShowUntil, p.Status, p.PublishAt, p.PublishedAt, p.UpdatedAt, p.LegacyPath,
            Info(p, original), Summaries(p, translations));
}

/// <summary>
/// What the app and the website read. Only live content; anyone may read it. Lists show the English originals;
/// a page or post can then be read in any language it has a published (so checked) translation in.
/// </summary>
public sealed class PublicContentService(IContentDb db, TimeProvider clock)
{
    private static readonly Error NotFound = Error.NotFound("content.not_found", "Not found.");

    public async Task<IReadOnlyList<MenuItemDto>> MenuAsync(CancellationToken cancellationToken) =>
        await db.Pages.AsNoTracking()
            .Where(p => p.Status == ContentStatus.Published && p.MenuOrder != null && p.TranslationOfId == null)
            .OrderBy(p => p.MenuOrder)
            .Select(p => new MenuItemDto(p.Title, p.Slug))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PageDto>> PagesAsync(CancellationToken cancellationToken)
    {
        var pages = await db.Pages.AsNoTracking()
            .Where(p => p.Status == ContentStatus.Published && p.TranslationOfId == null)
            .OrderBy(p => p.Title)
            .ToListAsync(cancellationToken);
        var languages = await LanguagesAsync(db.Pages, pages.Select(p => p.Id).ToList(), cancellationToken);
        return pages.Select(p => ToDto(p, languages[p.Id])).ToList();
    }

    public async Task<Result<PageDto>> PageAsync(string slug, string? lang, CancellationToken cancellationToken)
    {
        var language = lang ?? Languages.English;
        var page = await db.Pages.AsNoTracking().SingleOrDefaultAsync(p => p.Slug == slug && p.Language == language && p.Status == ContentStatus.Published, cancellationToken);
        if (page is null)
        {
            return NotFound;
        }

        var languages = await LanguagesAsync(db.Pages, [page.TranslationOfId ?? page.Id], cancellationToken);
        return ToDto(page, languages[page.TranslationOfId ?? page.Id]);
    }

    public async Task<PostPageDto> PostsAsync(PostKind? kind, int page, int pageSize, CancellationToken cancellationToken)
    {
        pageSize = Math.Clamp(pageSize, 1, 50);
        page = Math.Max(1, page);
        var query = db.Posts.AsNoTracking().Where(p => p.Status == ContentStatus.Published && p.TranslationOfId == null);
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
            .Where(p => p.Kind == PostKind.News && p.Status == ContentStatus.Published && p.TranslationOfId == null && (p.ShowUntil == null || p.ShowUntil >= today))
            .OrderByDescending(p => p.PublishedAt)
            .Take(Math.Clamp(count, 1, 10))
            .Select(Summary)
            .ToListAsync(cancellationToken);
    }

    public async Task<Result<PostDto>> PostAsync(string slug, string? lang, CancellationToken cancellationToken)
    {
        var language = lang ?? Languages.English;
        var post = await db.Posts.AsNoTracking().SingleOrDefaultAsync(p => p.Slug == slug && p.Language == language && p.Status == ContentStatus.Published, cancellationToken);
        if (post is null)
        {
            return NotFound;
        }

        var originalId = post.TranslationOfId ?? post.Id;
        var languages = await LanguagesAsync(db.Posts, [originalId], cancellationToken);
        return new PostDto(
            new PostSummaryDto(post.Id, post.Kind, post.Title, post.Slug, post.Summary, post.Author, post.CoverImageUrl, post.PublishedAt!.Value),
            post.Body,
            post.Language,
            languages[originalId]);
    }

    /// <summary>Old WordPress addresses of everything imported, and where each now lives.</summary>
    public async Task<IReadOnlyList<RedirectDto>> RedirectsAsync(CancellationToken cancellationToken)
    {
        var pages = await db.Pages.AsNoTracking().Where(p => p.LegacyPath != null && p.Status == ContentStatus.Published && p.TranslationOfId == null)
            .Select(p => new RedirectDto(p.LegacyPath!, "/" + p.Slug)).ToListAsync(cancellationToken);
        var posts = await db.Posts.AsNoTracking().Where(p => p.LegacyPath != null && p.Status == ContentStatus.Published && p.TranslationOfId == null)
            .Select(p => new RedirectDto(p.LegacyPath!, "/blog/" + p.Slug)).ToListAsync(cancellationToken);
        return [.. pages, .. posts];
    }

    /// <summary>For each original: English plus every language it has a published translation in, in the platform's order.</summary>
    private static async Task<Dictionary<Guid, IReadOnlyList<LanguageDto>>> LanguagesAsync<T>(DbSet<T> set, IReadOnlyCollection<Guid> originalIds, CancellationToken cancellationToken)
        where T : PublishableContent
    {
        var translated = await set.AsNoTracking()
            .Where(t => t.TranslationOfId != null && originalIds.Contains(t.TranslationOfId.Value) && t.Status == ContentStatus.Published)
            .Select(t => new { OriginalId = t.TranslationOfId!.Value, t.Language })
            .ToListAsync(cancellationToken);
        return originalIds.ToDictionary(
            id => id,
            id =>
            {
                var codes = translated.Where(t => t.OriginalId == id).Select(t => t.Language).Append(Languages.English).ToHashSet();
                return (IReadOnlyList<LanguageDto>)Languages.All.Where(l => codes.Contains(l.Code)).Select(l => new LanguageDto(l.Code, l.Name)).ToList();
            });
    }

    private static PageDto ToDto(Page p, IReadOnlyList<LanguageDto> languages) =>
        new(p.Id, p.Title, p.Slug, p.Summary, p.Body, p.MenuOrder, p.UpdatedAt, p.Language, languages);

    private static readonly System.Linq.Expressions.Expression<Func<Post, PostSummaryDto>> Summary =
        p => new PostSummaryDto(p.Id, p.Kind, p.Title, p.Slug, p.Summary, p.Author, p.CoverImageUrl, p.PublishedAt!.Value);
}

/// <summary>Gives AI translation drafts the English original of a page or post.</summary>
public sealed class ContentSource(IContentDb db) : IContentSource
{
    public async Task<ContentForTranslation?> GetForTranslationAsync(ContentType type, Guid id, CancellationToken cancellationToken)
    {
        PublishableContent? item = type == ContentType.Page
            ? await db.Pages.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id && p.TranslationOfId == null, cancellationToken)
            : await db.Posts.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id && p.TranslationOfId == null, cancellationToken);
        return item is null ? null : new ContentForTranslation(item.Id, type, item.Title, item.Summary, item.Body, item.Scope);
    }
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
