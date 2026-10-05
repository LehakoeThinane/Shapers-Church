using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shapers.Platform.Authorization;
using Shapers.Platform.Modules;
using Shapers.Platform.Web;
using Shapers.Privacy.Application;
using Shapers.Privacy.Contracts;
using Shapers.Privacy.Domain;
using Shapers.Privacy.Infrastructure;

namespace Shapers.Privacy.Api;

public sealed class PrivacyModule : IModule
{
    public string Name => "privacy";

    public void AddServices(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment) =>
        services.AddPrivacyInfrastructure(configuration);

    public Task InitialiseAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.InitialisePrivacyAsync(cancellationToken);

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/privacy/notice", (PrivacyNoticeService service) => service.Current())
            .WithTags("Privacy")
            .WithName("PrivacyNotice")
            .AllowAnonymous();

        var me = endpoints.MapGroup("/api/me").WithTags("Privacy").RequireAuthorization();
        me.MapGet("/privacy-status", async (PrivacyNoticeService service, CancellationToken ct) => (await service.StatusAsync(ct)).ToHttp())
            .WithName("MyPrivacyStatus");
        me.MapGet("/data-export", async Task<Results<FileContentHttpResult, ProblemHttpResult>> (MyPrivacyService service, CancellationToken ct) =>
            {
                var export = await service.ExportAsync(ct);
                return export.IsSuccess
                    ? TypedResults.File(export.Value, "application/json", "shapers-church-my-data.json")
                    : export.Error!.ToProblem();
            })
            .WithName("DownloadMyData")
            .RequireRateLimiting(RateLimitPolicies.Auth);
        me.MapGet("/privacy-requests", async (MyPrivacyService service, CancellationToken ct) => (await service.MineAsync(ct)).ToHttp())
            .WithName("MyPrivacyRequests");
        me.MapPost("/privacy-requests", async (SubmitDataRequest request, MyPrivacyService service, CancellationToken ct) => (await service.SubmitAsync(request, ct)).ToHttp())
            .WithName("SubmitPrivacyRequest");

        var admin = endpoints.MapGroup("/api/admin/privacy").WithTags("Privacy admin").RequireAuthorization();
        admin.MapGet("/requests", (DataRequestStatus? status, DataRequestAdminService service, CancellationToken ct) => service.ListAsync(status, ct))
            .WithName("ListPrivacyRequests")
            .RequirePermission(PrivacyPermissions.RequestsManage);
        admin.MapPost("/requests/{id:guid}/complete", async (Guid id, DecideDataRequest request, DataRequestAdminService service, CancellationToken ct) =>
                (await service.CompleteAsync(id, request, ct)).ToHttp())
            .WithName("CompletePrivacyRequest")
            .RequirePermission(PrivacyPermissions.RequestsManage);
        admin.MapPost("/requests/{id:guid}/decline", async (Guid id, DecideDataRequest request, DataRequestAdminService service, CancellationToken ct) =>
                (await service.DeclineAsync(id, request, ct)).ToHttp())
            .WithName("DeclinePrivacyRequest")
            .RequirePermission(PrivacyPermissions.RequestsManage);

        var breaches = admin.MapGroup("/breaches");
        breaches.MapGet("/", (BreachService service, CancellationToken ct) => service.ListAsync(ct))
            .WithName("ListBreaches")
            .RequirePermission(PrivacyPermissions.BreachesManage);
        breaches.MapPost("/", async (SaveBreachRequest request, BreachService service, CancellationToken ct) =>
                (await service.RecordAsync(request, ct)).ToCreated(b => $"/api/admin/privacy/breaches/{b.Id}"))
            .WithName("RecordBreach")
            .RequirePermission(PrivacyPermissions.BreachesManage);
        breaches.MapPut("/{id:guid}", async (Guid id, SaveBreachRequest request, BreachService service, CancellationToken ct) =>
                (await service.UpdateAsync(id, request, ct)).ToHttp())
            .WithName("UpdateBreach")
            .RequirePermission(PrivacyPermissions.BreachesManage);
        breaches.MapPost("/{id:guid}/close", async (Guid id, BreachService service, CancellationToken ct) => (await service.CloseAsync(id, ct)).ToHttp())
            .WithName("CloseBreach")
            .RequirePermission(PrivacyPermissions.BreachesManage);
        breaches.MapPost("/{id:guid}/reopen", async (Guid id, BreachService service, CancellationToken ct) => (await service.ReopenAsync(id, ct)).ToHttp())
            .WithName("ReopenBreach")
            .RequirePermission(PrivacyPermissions.BreachesManage);
    }
}
