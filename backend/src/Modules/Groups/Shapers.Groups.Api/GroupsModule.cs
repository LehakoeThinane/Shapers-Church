using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shapers.Groups.Application;
using Shapers.Groups.Contracts;
using Shapers.Groups.Infrastructure;
using Shapers.Platform.Authorization;
using Shapers.Platform.Modules;
using Shapers.Platform.Web;

namespace Shapers.Groups.Api;

public sealed class GroupsModule : IModule
{
    public string Name => "groups";

    public void AddServices(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment) =>
        services.AddGroupsInfrastructure(configuration);

    public Task InitialiseAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.InitialiseGroupsAsync(cancellationToken);

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // Members: the cells they belong to and what their leaders shared.
        endpoints.MapGet("/api/me/cells", async (MyCellsService service, CancellationToken ct) => (await service.MineAsync(ct)).ToHttp())
            .WithTags("Cells").WithName("MyCells").RequireAuthorization();

        // Leaders: only cells they lead, checked per request (and with a two-step sign-in).
        var lead = endpoints.MapGroup("/api/cells/{cellId:guid}").WithTags("Cells (leaders)").RequireAuthorization();
        lead.MapGet("/", async (Guid cellId, CellLeaderService s, CancellationToken ct) => (await s.GetAsync(cellId, ct)).ToHttp()).WithName("LeaderCell");
        lead.MapPost("/members", async (Guid cellId, NewMemberRequest request, CellLeaderService s, CancellationToken ct) => (await s.AddNewMemberAsync(cellId, request, ct)).ToHttp())
            .WithName("LeaderAddMember");
        lead.MapDelete("/members/{personId:guid}", async (Guid cellId, Guid personId, CellLeaderService s, CancellationToken ct) => (await s.RemoveMemberAsync(cellId, personId, ct)).ToHttp())
            .WithName("LeaderRemoveMember");
        lead.MapGet("/reports", async (Guid cellId, CellLeaderService s, CancellationToken ct) => (await s.ReportsAsync(cellId, ct)).ToHttp()).WithName("LeaderReports");
        lead.MapGet("/reports/{reportId:guid}", async (Guid cellId, Guid reportId, CellLeaderService s, CancellationToken ct) => (await s.ReportAsync(cellId, reportId, ct)).ToHttp())
            .WithName("LeaderReport");
        lead.MapPost("/reports", async (Guid cellId, SaveReportRequest request, CellLeaderService s, CancellationToken ct) => (await s.CreateReportAsync(cellId, request, ct)).ToHttp())
            .WithName("LeaderCreateReport");
        lead.MapPut("/reports/{reportId:guid}", async (Guid cellId, Guid reportId, SaveReportRequest request, CellLeaderService s, CancellationToken ct) =>
                (await s.UpdateReportAsync(cellId, reportId, request, ct)).ToHttp())
            .WithName("LeaderUpdateReport");
        lead.MapGet("/materials", async (Guid cellId, CellLeaderService s, CancellationToken ct) => (await s.MaterialsAsync(cellId, ct)).ToHttp()).WithName("LeaderMaterials");
        lead.MapPost("/materials", async (Guid cellId, SaveMaterialRequest request, CellLeaderService s, CancellationToken ct) => (await s.CreateMaterialAsync(cellId, request, ct)).ToHttp())
            .WithName("LeaderCreateMaterial");
        lead.MapPut("/materials/{materialId:guid}", async (Guid cellId, Guid materialId, SaveMaterialRequest request, CellLeaderService s, CancellationToken ct) =>
                (await s.UpdateMaterialAsync(cellId, materialId, request, ct)).ToHttp())
            .WithName("LeaderUpdateMaterial");
        lead.MapDelete("/materials/{materialId:guid}", async (Guid cellId, Guid materialId, CellLeaderService s, CancellationToken ct) =>
                (await s.DeleteMaterialAsync(cellId, materialId, ct)).ToHttp())
            .WithName("LeaderDeleteMaterial");

        // Pastors and administrators.
        var admin = endpoints.MapGroup("/api/admin/cells").WithTags("Cells admin").RequireAuthorization();
        admin.MapGet("/", (bool? includeClosed, CellAdminService s, CancellationToken ct) => s.ListAsync(includeClosed ?? false, ct))
            .WithName("ListCells")
            .RequireAnyPermission(GroupsPermissions.CellsManage, GroupsPermissions.ReportsView);
        admin.MapPost("/", async (SaveCellRequest request, CellAdminService s, CancellationToken ct) => (await s.CreateAsync(request, ct)).ToHttp())
            .WithName("CreateCell")
            .RequirePermission(GroupsPermissions.CellsManage);
        admin.MapGet("/reports", (Guid? cellId, bool? urgentOnly, CellReportsService s, CancellationToken ct) => s.ListAsync(cellId, urgentOnly ?? false, ct))
            .WithName("ListCellReports")
            .RequirePermission(GroupsPermissions.ReportsView);
        admin.MapGet("/reports/{id:guid}", async (Guid id, CellReportsService s, CancellationToken ct) => (await s.GetAsync(id, ct)).ToHttp())
            .WithName("GetCellReport")
            .RequirePermission(GroupsPermissions.ReportsView);
        admin.MapPost("/reports/{id:guid}/follow-ups/{followUpId:guid}/resolve", async (Guid id, Guid followUpId, CellReportsService s, CancellationToken ct) =>
                (await s.ResolveFollowUpAsync(id, followUpId, ct)).ToHttp())
            .WithName("ResolveCellFollowUp")
            .RequirePermission(GroupsPermissions.ReportsView);
        admin.MapGet("/materials", (Guid? cellId, CellReportsService s, CancellationToken ct) => s.MaterialsAsync(cellId, ct))
            .WithName("ListCellMaterials")
            .RequirePermission(GroupsPermissions.ReportsView);
        admin.MapGet("/{id:guid}", async (Guid id, CellAdminService s, CancellationToken ct) => (await s.GetAsync(id, ct)).ToHttp())
            .WithName("GetCell")
            .RequireAnyPermission(GroupsPermissions.CellsManage, GroupsPermissions.ReportsView);
        admin.MapPut("/{id:guid}", async (Guid id, SaveCellRequest request, CellAdminService s, CancellationToken ct) => (await s.UpdateAsync(id, request, ct)).ToHttp())
            .WithName("UpdateCell")
            .RequirePermission(GroupsPermissions.CellsManage);
        admin.MapPost("/{id:guid}/close", async (Guid id, CellAdminService s, CancellationToken ct) => (await s.CloseAsync(id, ct)).ToHttp())
            .WithName("CloseCell")
            .RequirePermission(GroupsPermissions.CellsManage);
        admin.MapPost("/{id:guid}/members", async (Guid id, AddMemberRequest request, CellAdminService s, CancellationToken ct) => (await s.AddMemberAsync(id, request, ct)).ToHttp())
            .WithName("AddCellMember")
            .RequirePermission(GroupsPermissions.CellsManage);
        admin.MapDelete("/{id:guid}/members/{personId:guid}", async (Guid id, Guid personId, CellAdminService s, CancellationToken ct) =>
                (await s.RemoveMemberAsync(id, personId, ct)).ToHttp())
            .WithName("RemoveCellMember")
            .RequirePermission(GroupsPermissions.CellsManage);
    }
}
