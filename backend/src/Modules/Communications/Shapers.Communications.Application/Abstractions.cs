using Microsoft.EntityFrameworkCore;
using Shapers.Communications.Contracts;
using Shapers.Communications.Domain;
using Shapers.Platform.Authorization;

namespace Shapers.Communications.Application;

public interface ICommunicationsDb
{
    DbSet<Device> Devices { get; }

    DbSet<TopicPreference> Preferences { get; }

    DbSet<Notification> Notifications { get; }

    DbSet<Announcement> Announcements { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public sealed record PushMessage(string Token, string Title, string Body, string? Link);

/// <summary>The provider's answer for one message. DeviceGone means the app was uninstalled or the token revoked.</summary>
public sealed record PushResult(bool Ok, string? ProviderReference, bool DeviceGone, string? Error);

/// <summary>Sends push notifications (Expo's push service, which relays to FCM and APNs).</summary>
public interface IPushSender
{
    /// <summary>Results are in the same order as the messages.</summary>
    Task<IReadOnlyList<PushResult>> SendAsync(IReadOnlyList<PushMessage> messages, CancellationToken cancellationToken);
}

public sealed class PushOptions
{
    public const string SectionName = "Communications:Push";

    /// <summary>"Expo" sends for real; "Log" writes to the console (tests, or machines without internet).</summary>
    public string Provider { get; set; } = "Expo";

    /// <summary>Optional Expo access token, required once "enhanced push security" is on for the project.</summary>
    public string? AccessToken { get; set; }
}

public sealed class CommunicationsPermissionProvider : IPermissionProvider
{
    public IEnumerable<PermissionDefinition> GetPermissions() =>
    [
        new(CommunicationsPermissions.DeliveriesView, "communications", "See the notification delivery log", IsSensitive: true),
        new(CommunicationsPermissions.AnnouncementsSend, "communications", "Write announcements and send them to a ministry"),
        new(CommunicationsPermissions.AnnouncementsApprove, "communications", "Approve announcements for a campus or the whole church"),
    ];
}

public sealed record RegisterDeviceRequest(string Token, string Platform, string? Name);

public sealed record UnregisterDeviceRequest(string Token);

public sealed record NotificationDto(Guid Id, Topic Topic, string Title, string Body, string? Link, DateTimeOffset CreatedAt, bool Read);

public sealed record InboxDto(IReadOnlyList<NotificationDto> Items, int Unread);

/// <summary>One switch in the app's settings. ConsentGiven is false when the member hasn't agreed to that channel at all.</summary>
public sealed record PreferenceDto(Topic Topic, Channel Channel, bool Enabled, bool ConsentGiven);

public sealed record SetPreferenceRequest(Topic Topic, Channel Channel, bool Enabled);

public sealed record DeliveryLogDto(Guid NotificationId, Guid PersonId, Topic Topic, string Title, Channel Channel, DeliveryStatus Status, int Attempts, DateTimeOffset CreatedAt, DateTimeOffset? SentAt, string? Error);
