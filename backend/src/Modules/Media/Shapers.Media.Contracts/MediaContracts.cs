namespace Shapers.Media.Contracts;

public static class MediaPermissions
{
    /// <summary>Create and edit sermons, series and uploads; see drafts.</summary>
    public const string SermonsEdit = "media.sermons.edit";

    /// <summary>Publish, schedule, unpublish and archive.</summary>
    public const string SermonsPublish = "media.sermons.publish";

    public const string SpeakersManage = "media.speakers.manage";

    /// <summary>Schedule streams, go live, put scripture on screen.</summary>
    public const string LivestreamManage = "media.livestream.manage";

    /// <summary>Hide and approve chat messages, time people out or ban them, set slow mode and the word list.</summary>
    public const string ChatModerate = "media.chat.moderate";
}

/// <summary>
/// Someone reported a chat message during a service. Notifications alert the moderators for that scope.
/// Carries no text: the message is read in the moderator console.
/// </summary>
public sealed record ChatMessageReportedIntegrationEvent(Guid MessageId, Guid LivestreamId, string Scope) : IntegrationEvent;

/// <summary>A sermon became public. Notifications will announce it; search will index it.</summary>
public sealed record SermonPublishedIntegrationEvent(Guid SermonId, string Title, string Slug, string Scope) : IntegrationEvent;

/// <summary>A service is live now. Notifications will send "We're live".</summary>
public sealed record LivestreamStartedIntegrationEvent(Guid LivestreamId, string Title, string Scope) : IntegrationEvent;
