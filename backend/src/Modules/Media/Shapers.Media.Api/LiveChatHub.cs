using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Shapers.Media.Application;

namespace Shapers.Media.Api;

/// <summary>
/// Receive-only: viewers join a livestream's room and are sent new messages, removals and rule changes. Posting,
/// reporting and moderating go through the ordinary HTTP endpoints, where sign-in, permissions and slow mode apply.
/// </summary>
[AllowAnonymous]
public sealed class LiveChatHub : Hub
{
    public const string Path = "/hubs/live-chat";

    public Task Join(Guid livestreamId) => Groups.AddToGroupAsync(Context.ConnectionId, Room(livestreamId));

    public Task Leave(Guid livestreamId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, Room(livestreamId));

    internal static string Room(Guid livestreamId) => $"live:{livestreamId:N}";
}

internal sealed class SignalRChatBroadcaster(IHubContext<LiveChatHub> hub) : IChatBroadcaster
{
    public Task MessageAsync(Guid livestreamId, ChatMessageDto message, CancellationToken cancellationToken) =>
        hub.Clients.Group(LiveChatHub.Room(livestreamId)).SendAsync("message", message, cancellationToken);

    public Task RemovedAsync(Guid livestreamId, Guid messageId, CancellationToken cancellationToken) =>
        hub.Clients.Group(LiveChatHub.Room(livestreamId)).SendAsync("removed", messageId, cancellationToken);

    public Task RulesAsync(Guid livestreamId, ChatRulesDto rules, CancellationToken cancellationToken) =>
        hub.Clients.Group(LiveChatHub.Room(livestreamId)).SendAsync("rules", rules, cancellationToken);
}
