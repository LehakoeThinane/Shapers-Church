using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shapers.Events.Application;
using Shapers.Events.Contracts;
using Shapers.Events.Infrastructure;
using Shapers.Platform.Authorization;
using Shapers.Platform.Modules;
using Shapers.Platform.Web;

namespace Shapers.Events.Api;

public sealed class EventsModule : IModule
{
    public string Name => "events";

    public void AddServices(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment) =>
        services.AddEventsInfrastructure(configuration);

    public Task InitialiseAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.InitialiseEventsAsync(cancellationToken);

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var events = endpoints.MapGroup("/api/events").WithTags("Events").AllowAnonymous();
        events.MapGet("/", (PublicEventService service, CancellationToken ct) => service.UpcomingAsync(ct)).WithName("ListEvents");
        events.MapGet("/{slug}", async (string slug, PublicEventService service, CancellationToken ct) => (await service.GetAsync(slug, ct)).ToHttp())
            .WithName("GetEvent");

        // Guests (public website): verify an email, then register. Rate-limited like sign-in.
        events.MapPost("/{slug}/guest-code", async (string slug, GuestCodeRequest request, RegistrationService service, CancellationToken ct) =>
                (await service.SendGuestCodeAsync(slug, request, ct)).ToHttp())
            .WithName("SendGuestCode")
            .RequireRateLimiting(RateLimitPolicies.Auth);
        events.MapPost("/{slug}/register-guest", async (string slug, GuestRegisterRequest request, RegistrationService service, CancellationToken ct) =>
                (await service.RegisterGuestAsync(slug, request, ct)).ToHttp())
            .WithName("RegisterGuest")
            .RequireRateLimiting(RateLimitPolicies.Auth);
        events.MapGet("/registrations/{id:guid}", async (Guid id, string key, RegistrationService service, CancellationToken ct) =>
                (await service.GetGuestAsync(id, key, ct)).ToHttp())
            .WithName("GetGuestRegistration")
            .RequireRateLimiting(RateLimitPolicies.Auth);
        events.MapPost("/registrations/{id:guid}/cancel", async (Guid id, string key, RegistrationService service, CancellationToken ct) =>
                (await service.CancelGuestAsync(id, key, ct)).ToHttp())
            .WithName("CancelGuestRegistration")
            .RequireRateLimiting(RateLimitPolicies.Auth);

        // Members (app).
        endpoints.MapPost("/api/events/{slug}/register", async (string slug, MemberRegisterRequest request, RegistrationService service, CancellationToken ct) =>
                (await service.RegisterMemberAsync(slug, request, ct)).ToHttp())
            .WithTags("Events")
            .WithName("RegisterForEvent")
            .RequireAuthorization();
        var me = endpoints.MapGroup("/api/me/registrations").WithTags("Events").RequireAuthorization();
        me.MapGet("/", (RegistrationService service, CancellationToken ct) => service.MineAsync(ct)).WithName("MyRegistrations");
        me.MapPost("/{id:guid}/cancel", async (Guid id, RegistrationService service, CancellationToken ct) => (await service.CancelMineAsync(id, ct)).ToHttp())
            .WithName("CancelMyRegistration");

        MapAdmin(endpoints.MapGroup("/api/admin/events").WithTags("Events admin").RequireAuthorization());
    }

    private static void MapAdmin(RouteGroupBuilder admin)
    {
        admin.MapGet("/", (EventAdminService service, CancellationToken ct) => service.ListAsync(ct)).WithName("AdminListEvents");
        admin.MapGet("/{id:guid}", async (Guid id, EventAdminService service, CancellationToken ct) => (await service.GetAsync(id, ct)).ToHttp())
            .WithName("AdminGetEvent");
        admin.MapPost("/", async (SaveEventRequest request, EventAdminService service, CancellationToken ct) =>
                (await service.CreateAsync(request, ct)).ToCreated(e => $"/api/admin/events/{e.Event.Id}"))
            .WithName("CreateEvent")
            .RequirePermission(EventsPermissions.Edit);
        admin.MapPut("/{id:guid}", async (Guid id, SaveEventRequest request, EventAdminService service, CancellationToken ct) =>
                (await service.UpdateAsync(id, request, ct)).ToHttp())
            .WithName("UpdateEvent")
            .RequirePermission(EventsPermissions.Edit);
        admin.MapPost("/{id:guid}/publish", async (Guid id, EventAdminService service, CancellationToken ct) => (await service.PublishAsync(id, ct)).ToHttp())
            .WithName("PublishEvent")
            .RequirePermission(EventsPermissions.Publish);
        admin.MapPost("/{id:guid}/unpublish", async (Guid id, EventAdminService service, CancellationToken ct) => (await service.UnpublishAsync(id, ct)).ToHttp())
            .WithName("UnpublishEvent")
            .RequirePermission(EventsPermissions.Publish);
        admin.MapPost("/{id:guid}/cancel", async (Guid id, EventAdminService service, CancellationToken ct) => (await service.CancelAsync(id, ct)).ToHttp())
            .WithName("CancelEvent")
            .RequirePermission(EventsPermissions.Publish);

        admin.MapGet("/{id:guid}/attendees", async (Guid id, RegistrationService service, CancellationToken ct) => (await service.AttendeesAsync(id, ct)).ToHttp())
            .WithName("ListAttendees")
            .RequirePermission(EventsPermissions.RegistrationsView);
        admin.MapGet("/{id:guid}/attendees.csv", async (Guid id, RegistrationService service, CancellationToken ct) =>
            {
                var csv = await service.ExportCsvAsync(id, ct);
                return csv.IsSuccess
                    ? Results.File(System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(csv.Value)).ToArray(), "text/csv", "attendees.csv")
                    : csv.Error!.ToProblem();
            })
            .WithName("ExportAttendees")
            .RequirePermission(EventsPermissions.RegistrationsView);
        admin.MapPost("/{id:guid}/registrations", async (Guid id, AdminRegisterRequest request, RegistrationService service, CancellationToken ct) =>
                (await service.RegisterByStaffAsync(id, request, ct)).ToHttp())
            .WithName("RegisterByStaff")
            .RequirePermission(EventsPermissions.RegistrationsManage);
        admin.MapPost("/registrations/{registrationId:guid}/cancel", async (Guid registrationId, RegistrationService service, CancellationToken ct) =>
                (await service.CancelByStaffAsync(registrationId, ct)).ToHttp())
            .WithName("CancelByStaff")
            .RequirePermission(EventsPermissions.RegistrationsManage);

        admin.MapPost("/{id:guid}/check-in", async (Guid id, CheckInRequest request, RegistrationService service, CancellationToken ct) =>
                (await service.CheckInAsync(id, request, ct)).ToHttp())
            .WithName("CheckIn")
            .RequirePermission(EventsPermissions.CheckIn);
        admin.MapGet("/{id:guid}/door-list", async (Guid id, string? q, RegistrationService service, CancellationToken ct) =>
                (await service.DoorListAsync(id, q, ct)).ToHttp())
            .WithName("DoorList")
            .RequirePermission(EventsPermissions.CheckIn);
    }
}
