namespace Shapers.Content.Contracts;

public static class ContentPermissions
{
    /// <summary>Write and edit pages, news and blog posts.</summary>
    public const string Edit = "content.edit";

    /// <summary>Publish, schedule, unpublish and archive them.</summary>
    public const string Publish = "content.publish";
}

/// <summary>A page or post went live. The website rebuilds so it appears.</summary>
public sealed record ContentPublishedIntegrationEvent(Guid ContentId, string Kind, string Slug) : IntegrationEvent;
