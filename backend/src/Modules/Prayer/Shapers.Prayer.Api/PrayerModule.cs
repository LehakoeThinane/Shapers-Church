using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shapers.Platform.Authorization;
using Shapers.Platform.Modules;
using Shapers.Platform.Web;
using Shapers.Prayer.Application;
using Shapers.Prayer.Contracts;
using Shapers.Prayer.Domain;
using Shapers.Prayer.Infrastructure;

namespace Shapers.Prayer.Api;

public sealed class PrayerModule : IModule
{
    public string Name => "prayer";

    public void AddServices(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment) =>
        services.AddPrayerInfrastructure(configuration);

    public Task InitialiseAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.InitialisePrayerAsync(cancellationToken);

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // Members only: the wall is for the church family, not the public website.
        var prayer = endpoints.MapGroup("/api/prayer").WithTags("Prayer").RequireAuthorization();
        prayer.MapGet("/wall", async (int? page, PrayerService service, CancellationToken ct) => (await service.WallAsync(page ?? 1, ct)).ToHttp())
            .WithName("PrayerWall");
        prayer.MapPost("/requests", async (SubmitPrayerRequest request, PrayerService service, CancellationToken ct) => (await service.SubmitAsync(request, ct)).ToHttp())
            .WithName("SubmitPrayerRequest");
        prayer.MapPost("/requests/{id:guid}/prayed", async (Guid id, PrayerService service, CancellationToken ct) => (await service.PrayedAsync(id, ct)).ToHttp())
            .WithName("IPrayed");

        var mine = endpoints.MapGroup("/api/me/prayer-requests").WithTags("Prayer").RequireAuthorization();
        mine.MapGet("/", async (PrayerService service, CancellationToken ct) => (await service.MineAsync(ct)).ToHttp()).WithName("MyPrayerRequests");
        mine.MapPost("/{id:guid}/answered", async (Guid id, AnswerRequest request, PrayerService service, CancellationToken ct) =>
                (await service.MarkAnsweredAsync(id, request, ct)).ToHttp())
            .WithName("MarkPrayerAnswered");
        mine.MapPost("/{id:guid}/withdraw", async (Guid id, PrayerService service, CancellationToken ct) => (await service.WithdrawAsync(id, ct)).ToHttp())
            .WithName("WithdrawPrayerRequest");

        var admin = endpoints.MapGroup("/api/admin/prayer").WithTags("Prayer admin").RequireAuthorization();
        admin.MapGet("/review", (PrayerAdminService service, CancellationToken ct) => service.AwaitingReviewAsync(ct))
            .WithName("PrayerReviewQueue")
            .RequirePermission(PrayerPermissions.RequestsModerate);
        admin.MapGet("/", (PrayerStatus? status, PrayerAdminService service, CancellationToken ct) => service.AllAsync(status, ct))
            .WithName("ListPrayerRequests")
            .RequirePermission(PrayerPermissions.RequestsView);
        admin.MapPost("/{id:guid}/approve", async (Guid id, ReviewRequest request, PrayerAdminService service, CancellationToken ct) =>
                (await service.ApproveAsync(id, request, ct)).ToHttp())
            .WithName("ApprovePrayerRequest")
            .RequirePermission(PrayerPermissions.RequestsModerate);
        admin.MapPost("/{id:guid}/keep-with-pastors", async (Guid id, ReviewRequest request, PrayerAdminService service, CancellationToken ct) =>
                (await service.KeepWithPastorsAsync(id, request, ct)).ToHttp())
            .WithName("KeepPrayerWithPastors")
            .RequirePermission(PrayerPermissions.RequestsModerate);
        admin.MapPost("/{id:guid}/take-down", async (Guid id, PrayerAdminService service, CancellationToken ct) => (await service.TakeDownAsync(id, ct)).ToHttp())
            .WithName("TakeDownPrayerRequest")
            .RequirePermission(PrayerPermissions.RequestsModerate);
    }
}
