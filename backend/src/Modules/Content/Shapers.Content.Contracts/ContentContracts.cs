namespace Shapers.Content.Contracts;

public static class ContentPermissions
{
    /// <summary>Write and edit pages, news and blog posts.</summary>
    public const string Edit = "content.edit";

    /// <summary>Publish, schedule, unpublish and archive them.</summary>
    public const string Publish = "content.publish";

    /// <summary>Check translations: a speaker of the language reads one and marks it checked. Only checked translations can be published.</summary>
    public const string TranslationsReview = "content.translations.review";
}

/// <summary>A page or post went live. The website rebuilds so it appears.</summary>
public sealed record ContentPublishedIntegrationEvent(Guid ContentId, string Kind, string Slug) : IntegrationEvent;

/// <summary>A page or a post: what can be translated.</summary>
public enum ContentType
{
    Page,
    Post,
}

/// <summary>The English text of a page or post, for drafting a translation. Public content only.</summary>
public sealed record ContentForTranslation(Guid Id, ContentType Type, string Title, string? Summary, string Body, string Scope);

/// <summary>Reads originals for other modules (AI translation drafts). The caller checks its own permissions at <see cref="ContentForTranslation.Scope"/>.</summary>
public interface IContentSource
{
    Task<ContentForTranslation?> GetForTranslationAsync(ContentType type, Guid id, CancellationToken cancellationToken);
}
