using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shapers.Church.Application;
using Shapers.Church.Contracts;
using Shapers.Church.Infrastructure;
using Shapers.Platform.Authorization;
using Shapers.Platform.Modules;
using Shapers.Platform.Web;

namespace Shapers.Church.Api;

public sealed class ChurchModule : IModule
{
    public string Name => "church";

    public void AddServices(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment) =>
        services.AddChurchInfrastructure(configuration);

    public Task InitialiseAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.InitialiseChurchAsync(cancellationToken);

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // Public: the member app and website show church details and campuses without signing in.
        endpoints.MapGet("/api/church", (ChurchService service, CancellationToken ct) => service.GetOverviewAsync(ct))
            .WithTags("Church")
            .WithName("GetChurch")
            .AllowAnonymous();

        var admin = endpoints.MapGroup("/api/admin").WithTags("Church").RequireAuthorization();

        admin.MapGet("/scopes", (IChurchDirectory directory, CancellationToken ct) => directory.GetScopesAsync(ct))
            .WithName("ListScopes");

        admin.MapGet("/campuses", (ChurchService service, CancellationToken ct) => service.ListCampusesAsync(ct))
            .WithName("ListCampuses");

        admin.MapPost("/campuses", async (CreateCampusRequest request, ChurchService service, CancellationToken ct) =>
                (await service.CreateCampusAsync(request, ct)).ToCreated(c => $"/api/admin/campuses/{c.Id}"))
            .WithName("CreateCampus")
            .RequirePermission(ChurchPermissions.CampusesManage);

        admin.MapPut("/campuses/{id:guid}", async (Guid id, UpdateCampusRequest request, ChurchService service, CancellationToken ct) =>
                (await service.UpdateCampusAsync(id, request, ct)).ToHttp())
            .WithName("UpdateCampus")
            .RequirePermission(ChurchPermissions.CampusesManage);

        admin.MapGet("/ministries", (ChurchService service, CancellationToken ct) => service.ListMinistriesAsync(ct))
            .WithName("ListMinistries");

        admin.MapPost("/ministries", async (CreateMinistryRequest request, ChurchService service, CancellationToken ct) =>
                (await service.CreateMinistryAsync(request, ct)).ToCreated(m => $"/api/admin/ministries/{m.Id}"))
            .WithName("CreateMinistry")
            .RequirePermission(ChurchPermissions.MinistriesManage);
    }
}
