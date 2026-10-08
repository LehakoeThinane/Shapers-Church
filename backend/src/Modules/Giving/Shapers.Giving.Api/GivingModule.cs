using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shapers.Giving.Application;
using Shapers.Giving.Contracts;
using Shapers.Giving.Domain;
using Shapers.Giving.Infrastructure;
using Shapers.Platform.Authorization;
using Shapers.Platform.Modules;
using Shapers.Platform.Web;

namespace Shapers.Giving.Api;

public sealed class GivingModule : IModule
{
    public string Name => "giving";

    public void AddServices(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment) =>
        services.AddGivingInfrastructure(configuration);

    public Task InitialiseAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.InitialiseGivingAsync(cancellationToken);

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // Public: the giving page and starting a card gift work for visitors as well as members.
        var giving = endpoints.MapGroup("/api/giving").WithTags("Giving");
        giving.MapGet("/", (GivingService service, CancellationToken ct) => service.PageAsync(ct))
            .WithName("GivingPage")
            .AllowAnonymous();
        giving.MapPost("/checkout", async (StartGiftRequest request, GivingService service, CancellationToken ct) => (await service.StartAsync(request, ct)).ToHttp())
            .WithName("StartGift")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Auth);

        // The payment provider's notifications. Anyone can call this address, so only a correctly signed body counts.
        giving.MapPost("/webhooks/yoco", async (HttpRequest request, GivingService service, CancellationToken ct) =>
            {
                using var reader = new StreamReader(request.Body);
                var body = await reader.ReadToEndAsync(ct);
                var headers = request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);
                return await service.HandleNoticeAsync(headers, body, ct) ? Results.NoContent() : Results.Unauthorized();
            })
            .WithName("YocoWebhook")
            .AllowAnonymous()
            .ExcludeFromDescription();

        endpoints.MapGet("/api/me/giving", async (GivingService service, CancellationToken ct) => (await service.MineAsync(ct)).ToHttp())
            .WithTags("Giving")
            .WithName("MyGiving")
            .RequireAuthorization();

        // The finance team. Giving is church-wide: the permission must be held at the church's root.
        var admin = endpoints.MapGroup("/api/admin/giving").WithTags("Giving admin").RequireAuthorization();
        admin.MapGet("/overview", async (string? month, GivingAdminService s, CancellationToken ct) => (await s.OverviewAsync(month, ct)).ToHttp())
            .WithName("GivingOverview")
            .RequirePermission(GivingPermissions.View);
        admin.MapGet("/gifts", async (DateOnly? from, DateOnly? to, Guid? fundId, GiftMethod? method, string? search, int? page, GivingAdminService s, CancellationToken ct) =>
                (await s.ListAsync(from, to, fundId, method, search, page ?? 1, ct)).ToHttp())
            .WithName("ListGifts")
            .RequirePermission(GivingPermissions.View);
        admin.MapPost("/gifts", async (RecordGiftRequest request, GivingAdminService s, CancellationToken ct) => (await s.RecordAsync(request, ct)).ToHttp())
            .WithName("RecordGift")
            .RequirePermission(GivingPermissions.Manage);
        admin.MapGet("/statements/{personId:guid}", async (Guid personId, int year, GivingAdminService s, CancellationToken ct) => (await s.StatementAsync(personId, year, ct)).ToHttp())
            .WithName("GivingStatement")
            .RequirePermission(GivingPermissions.View);

        admin.MapGet("/funds", async (GivingAdminService s, CancellationToken ct) => (await s.FundsAsync(ct)).ToHttp())
            .WithName("ListFunds")
            .RequireAnyPermission(GivingPermissions.View, GivingPermissions.Manage);
        admin.MapPost("/funds", async (SaveFundRequest request, GivingAdminService s, CancellationToken ct) => (await s.CreateFundAsync(request, ct)).ToHttp())
            .WithName("CreateFund")
            .RequirePermission(GivingPermissions.Manage);
        admin.MapPut("/funds/{id:guid}", async (Guid id, SaveFundRequest request, GivingAdminService s, CancellationToken ct) => (await s.UpdateFundAsync(id, request, ct)).ToHttp())
            .WithName("UpdateFund")
            .RequirePermission(GivingPermissions.Manage);
        admin.MapPost("/funds/{id:guid}/archive", async (Guid id, GivingAdminService s, CancellationToken ct) => (await s.ArchiveFundAsync(id, ct)).ToHttp())
            .WithName("ArchiveFund")
            .RequirePermission(GivingPermissions.Manage);
        admin.MapPost("/funds/{id:guid}/restore", async (Guid id, GivingAdminService s, CancellationToken ct) => (await s.RestoreFundAsync(id, ct)).ToHttp())
            .WithName("RestoreFund")
            .RequirePermission(GivingPermissions.Manage);
    }
}
