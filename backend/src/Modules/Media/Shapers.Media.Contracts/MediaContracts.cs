namespace Shapers.Media.Contracts;

public static class MediaPermissions
{
    /// <summary>Create and edit sermons, series and uploads; see drafts.</summary>
    public const string SermonsEdit = "media.sermons.edit";

    /// <summary>Publish, schedule, unpublish and archive.</summary>
    public const string SermonsPublish = "media.sermons.publish";

    public const string SpeakersManage = "media.speakers.manage";
}

/// <summary>A sermon became public. Notifications will announce it; search will index it.</summary>
public sealed record SermonPublishedIntegrationEvent(Guid SermonId, string Title, string Slug, string Scope) : IntegrationEvent;
