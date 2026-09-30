using Shapers.Communications.Domain;
using Shapers.Events.Contracts;
using Shapers.Identity.Contracts;
using Shapers.Media.Contracts;
using Shapers.Platform.Messaging;
using Shapers.Prayer.Contracts;

namespace Shapers.Communications.Application;

// What other modules announce, turned into notifications. Wording lives here so the tone stays consistent.
// Prayer text and other personal details are never put in a notification: phones show them on the lock screen.

public sealed class NotifyLivestreamStarted(Notifier notifier) : IIntegrationEventHandler<LivestreamStartedIntegrationEvent>
{
    public Task HandleAsync(LivestreamStartedIntegrationEvent e, CancellationToken cancellationToken) =>
        notifier.ToAppUsersAsync(e.Scope, new Notifier.Message(Topic.Live, "We're live", $"{e.Title} has started. Join us now.", "/live", $"live:{e.LivestreamId}", Urgent: true), cancellationToken);
}

/// <summary>A chat message was reported during a service: the moderators for that campus are told straight away.</summary>
public sealed class NotifyChatReported(Notifier notifier, IUserDirectory users) : IIntegrationEventHandler<ChatMessageReportedIntegrationEvent>
{
    public async Task HandleAsync(ChatMessageReportedIntegrationEvent e, CancellationToken cancellationToken)
    {
        var moderators = await users.PeopleWithPermissionAsync(MediaPermissions.ChatModerate, e.Scope, cancellationToken);
        await notifier.ToPeopleAsync(moderators, new Notifier.Message(Topic.Live, "Chat message reported", "Someone reported a message in the live chat. Please check the moderator console.", null, $"chat-report:{e.MessageId}", Urgent: true), cancellationToken);
    }
}

public sealed class NotifySermonPublished(Notifier notifier) : IIntegrationEventHandler<SermonPublishedIntegrationEvent>
{
    public Task HandleAsync(SermonPublishedIntegrationEvent e, CancellationToken cancellationToken) =>
        notifier.ToAppUsersAsync(e.Scope, new Notifier.Message(Topic.Sermons, "New sermon", e.Title, $"/sermon/{e.Slug}", $"sermon:{e.SermonId}"), cancellationToken);
}

public sealed class NotifyWaitlistPromoted(Notifier notifier) : IIntegrationEventHandler<WaitlistPromotedIntegrationEvent>
{
    public Task HandleAsync(WaitlistPromotedIntegrationEvent e, CancellationToken cancellationToken) =>
        notifier.ToPeopleAsync([e.PersonId], new Notifier.Message(Topic.Events, "You're in!", "A seat opened up for an event you were waiting for. Your tickets are ready.", "/tickets", $"waitlist:{e.RegistrationId}"), cancellationToken);
}

public sealed class NotifyPrayerApproved(Notifier notifier) : IIntegrationEventHandler<PrayerRequestApprovedIntegrationEvent>
{
    public Task HandleAsync(PrayerRequestApprovedIntegrationEvent e, CancellationToken cancellationToken) =>
        notifier.ToPeopleAsync([e.PersonId], new Notifier.Message(Topic.Prayer, "Your request is on the prayer wall", "The church family can now pray with you.", "/prayer", $"prayer-approved:{e.RequestId}"), cancellationToken);
}
