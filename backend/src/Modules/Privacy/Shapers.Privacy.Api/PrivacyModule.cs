using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
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
        var me = endpoints.MapGroup("/api/me").WithTags("Privacy").RequireAuthorization();
        me.MapGet("/data-export", async (MyPrivacyService service, CancellationToken ct) =>
            {
                var export = await service.ExportAsync(ct);
                return export.IsSuccess
                    ? Results.File(export.Value, "application/json", "shapers-church-my-data.json")
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
    }
}
