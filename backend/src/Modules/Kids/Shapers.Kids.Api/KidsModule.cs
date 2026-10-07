using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shapers.Kids.Application;
using Shapers.Kids.Contracts;
using Shapers.Kids.Infrastructure;
using Shapers.Platform.Authorization;
using Shapers.Platform.Modules;
using Shapers.Platform.Web;

namespace Shapers.Kids.Api;

public sealed class KidsModule : IModule
{
    public string Name => "kids";

    public void AddServices(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment) =>
        services.AddKidsInfrastructure(configuration);

    public Task InitialiseAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.InitialiseKidsAsync(cancellationToken);

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // Parents in the app: their own children only.
        var mine = endpoints.MapGroup("/api/me/kids").WithTags("Kids").RequireAuthorization();
        mine.MapGet("/", async (ParentKidsService service, CancellationToken ct) => (await service.MyChildrenAsync(ct)).ToHttp())
            .WithName("MyKids");
        mine.MapPost("/", async (AddChildRequest request, ParentKidsService service, CancellationToken ct) => (await service.AddChildAsync(request, ct)).ToHttp())
            .WithName("AddMyChild");
        mine.MapPut("/{childId:guid}/care-notes", async (Guid childId, CareNotesRequest request, ParentKidsService service, CancellationToken ct) =>
                (await service.UpdateCareNotesAsync(childId, request, ct)).ToHttp())
            .WithName("UpdateMyChildCareNotes");
        mine.MapPost("/check-in", async (ParentCheckInRequest request, ParentKidsService service, CancellationToken ct) => (await service.CheckInAsync(request, ct)).ToHttp())
            .WithName("CheckInMyKids");

        // The kids team.
        var admin = endpoints.MapGroup("/api/admin/kids").WithTags("Kids admin").RequireAuthorization();
        admin.MapGet("/today", (KidsDeskService service, CancellationToken ct) => service.TodayAsync(ct))
            .WithName("KidsToday")
            .RequirePermission(KidsPermissions.CheckIn);
        admin.MapGet("/pickup/{code}", async (string code, KidsDeskService service, CancellationToken ct) => (await service.FindByCodeAsync(code, ct)).ToHttp())
            .WithName("FindKidsByPickupCode")
            .RequirePermission(KidsPermissions.CheckIn);
        admin.MapPost("/check-out", async (CheckOutRequest request, KidsDeskService service, CancellationToken ct) => (await service.CheckOutAsync(request, ct)).ToHttp())
            .WithName("CheckOutKids")
            .RequirePermission(KidsPermissions.CheckIn);
        admin.MapPost("/desk-check-in", async (DeskCheckInRequest request, KidsDeskService service, CancellationToken ct) => (await service.DeskCheckInAsync(request, ct)).ToHttp())
            .WithName("DeskCheckIn")
            .RequirePermission(KidsPermissions.CheckIn);
        admin.MapGet("/check-ins/{id:guid}/label", async (Guid id, KidsDeskService service, CancellationToken ct) => (await service.LabelAsync(id, ct)).ToHttp())
            .WithName("KidsLabel")
            .RequirePermission(KidsPermissions.CheckIn);
        admin.MapGet("/children/{childId:guid}/care-notes", async (Guid childId, KidsDeskService service, CancellationToken ct) => (await service.CareNotesAsync(childId, ct)).ToHttp())
            .WithName("KidsCareNotes")
            .RequirePermission(KidsPermissions.CareView);

        admin.MapGet("/classes", (KidsClassService service, CancellationToken ct) => service.ListAsync(ct))
            .WithName("ListKidsClasses")
            .RequireAnyPermission(KidsPermissions.CheckIn, KidsPermissions.Manage);
        admin.MapPost("/classes", async (SaveKidsClassRequest request, KidsClassService service, CancellationToken ct) => (await service.CreateAsync(request, ct)).ToHttp())
            .WithName("CreateKidsClass")
            .RequirePermission(KidsPermissions.Manage);
        admin.MapPut("/classes/{id:guid}", async (Guid id, SaveKidsClassRequest request, KidsClassService service, CancellationToken ct) => (await service.UpdateAsync(id, request, ct)).ToHttp())
            .WithName("UpdateKidsClass")
            .RequirePermission(KidsPermissions.Manage);
        admin.MapPost("/classes/{id:guid}/archive", async (Guid id, KidsClassService service, CancellationToken ct) => (await service.ArchiveAsync(id, ct)).ToHttp())
            .WithName("ArchiveKidsClass")
            .RequirePermission(KidsPermissions.Manage);
        admin.MapPost("/classes/{id:guid}/restore", async (Guid id, KidsClassService service, CancellationToken ct) => (await service.RestoreAsync(id, ct)).ToHttp())
            .WithName("RestoreKidsClass")
            .RequirePermission(KidsPermissions.Manage);
    }
}
