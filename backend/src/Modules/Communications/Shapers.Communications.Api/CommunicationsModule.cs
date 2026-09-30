using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shapers.Communications.Application;
using Shapers.Communications.Contracts;
using Shapers.Communications.Infrastructure;
using Shapers.Platform.Authorization;
using Shapers.Platform.Modules;
using Shapers.Platform.Web;

namespace Shapers.Communications.Api;

public sealed class CommunicationsModule : IModule
{
    public string Name => "communications";

    public void AddServices(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment) =>
        services.AddCommunicationsInfrastructure(configuration);

    public Task InitialiseAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.InitialiseCommunicationsAsync(cancellationToken);

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var me = endpoints.MapGroup("/api/me").WithTags("Notifications").RequireAuthorization();
        me.MapPost("/devices", async (RegisterDeviceRequest request, MemberNotifications service, CancellationToken ct) => (await service.RegisterDeviceAsync(request, ct)).ToHttp())
            .WithName("RegisterDevice");
        me.MapPost("/devices/unregister", async (UnregisterDeviceRequest request, MemberNotifications service, CancellationToken ct) => (await service.UnregisterDeviceAsync(request, ct)).ToHttp())
            .WithName("UnregisterDevice");
        me.MapGet("/notifications", async (int? page, MemberNotifications service, CancellationToken ct) => (await service.InboxAsync(page ?? 1, ct)).ToHttp())
            .WithName("MyNotifications");
        me.MapPost("/notifications/{id:guid}/read", async (Guid id, MemberNotifications service, CancellationToken ct) => (await service.MarkReadAsync(id, ct)).ToHttp())
            .WithName("MarkNotificationRead");
        me.MapPost("/notifications/read-all", async (MemberNotifications service, CancellationToken ct) => (await service.MarkReadAsync(null, ct)).ToHttp())
            .WithName("MarkAllNotificationsRead");
        me.MapGet("/notification-preferences", async (MemberNotifications service, CancellationToken ct) => (await service.PreferencesAsync(ct)).ToHttp())
            .WithName("MyNotificationPreferences");
        me.MapPut("/notification-preferences", async (SetPreferenceRequest request, MemberNotifications service, CancellationToken ct) => (await service.SetPreferenceAsync(request, ct)).ToHttp())
            .WithName("SetNotificationPreference");

        endpoints.MapGet("/api/admin/communications/deliveries", async (DeliveryLog log, CancellationToken ct) => (await log.RecentAsync(ct)).ToHttp())
            .WithTags("Notifications admin")
            .WithName("DeliveryLog")
            .RequireAuthorization()
            .RequirePermission(CommunicationsPermissions.DeliveriesView);
    }
}
