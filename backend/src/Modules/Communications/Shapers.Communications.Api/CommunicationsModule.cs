using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shapers.Communications.Application;
using Shapers.Communications.Contracts;
using Shapers.Communications.Domain;
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

        var announcements = endpoints.MapGroup("/api/admin/announcements").WithTags("Announcements").RequireAuthorization();
        announcements.MapGet("/", (AnnouncementService service, CancellationToken ct) => service.ListAsync(ct)).WithName("ListAnnouncements");
        announcements.MapGet("/{id:guid}", async (Guid id, AnnouncementService service, CancellationToken ct) => (await service.GetAsync(id, ct)).ToHttp())
            .WithName("GetAnnouncement");
        announcements.MapPost("/", async (SaveAnnouncementRequest request, AnnouncementService service, CancellationToken ct) =>
                (await service.CreateAsync(request, ct)).ToCreated(a => $"/api/admin/announcements/{a.Id}"))
            .WithName("CreateAnnouncement")
            .RequirePermission(CommunicationsPermissions.AnnouncementsSend);
        announcements.MapPut("/{id:guid}", async (Guid id, SaveAnnouncementRequest request, AnnouncementService service, CancellationToken ct) =>
                (await service.UpdateAsync(id, request, ct)).ToHttp())
            .WithName("UpdateAnnouncement")
            .RequirePermission(CommunicationsPermissions.AnnouncementsSend);
        announcements.MapPost("/{id:guid}/submit", async (Guid id, AnnouncementService service, CancellationToken ct) => (await service.SubmitAsync(id, ct)).ToHttp())
            .WithName("SubmitAnnouncement")
            .RequirePermission(CommunicationsPermissions.AnnouncementsSend);
        announcements.MapPost("/{id:guid}/approve", async (Guid id, AnnouncementService service, CancellationToken ct) => (await service.ApproveAsync(id, ct)).ToHttp())
            .WithName("ApproveAnnouncement")
            .RequirePermission(CommunicationsPermissions.AnnouncementsApprove);
        announcements.MapPost("/{id:guid}/return", async (Guid id, ReturnAnnouncementRequest request, AnnouncementService service, CancellationToken ct) =>
                (await service.ReturnToDraftAsync(id, request, ct)).ToHttp())
            .WithName("ReturnAnnouncementToDraft");
        announcements.MapPost("/{id:guid}/cancel", async (Guid id, AnnouncementService service, CancellationToken ct) => (await service.CancelAsync(id, ct)).ToHttp())
            .WithName("CancelAnnouncement")
            .RequirePermission(CommunicationsPermissions.AnnouncementsSend);

        // The link at the bottom of every announcement email. No sign-in: the signature proves it came from us.
        endpoints.MapGet("/api/unsubscribe", async (string p, Topic t, string s, Unsubscribe unsubscribe, CancellationToken ct) =>
            {
                var done = Guid.TryParseExact(p, "N", out var personId) && await unsubscribe.ApplyAsync(personId, t, s, ct);
                var message = done
                    ? "You won't receive these emails from Shapers Church any more. You can turn them back on in the app under Profile."
                    : "This unsubscribe link isn't valid. Please email info@shaperschurch.com and we'll take you off the list.";
                return Results.Content(UnsubscribePage(message), "text/html; charset=utf-8", statusCode: done ? 200 : 400);
            })
            .WithTags("Notifications")
            .WithName("Unsubscribe")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .ExcludeFromDescription();

        endpoints.MapGet("/api/admin/communications/deliveries", async (DeliveryLog log, CancellationToken ct) => (await log.RecentAsync(ct)).ToHttp())
            .WithTags("Notifications admin")
            .WithName("DeliveryLog")
            .RequireAuthorization()
            .RequirePermission(CommunicationsPermissions.DeliveriesView);
    }

    private static string UnsubscribePage(string message) =>
        $"<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">"
        + $"<title>Shapers Church</title></head><body style=\"font-family:system-ui,sans-serif;max-width:32rem;margin:4rem auto;padding:0 1rem;line-height:1.5\">"
        + $"<h1 style=\"font-size:1.4rem\">Shapers Church</h1><p>{System.Net.WebUtility.HtmlEncode(message)}</p></body></html>";
}
