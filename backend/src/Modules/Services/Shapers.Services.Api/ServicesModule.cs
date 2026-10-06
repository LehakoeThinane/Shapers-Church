using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shapers.Platform.Authorization;
using Shapers.Platform.Modules;
using Shapers.Platform.Web;
using Shapers.Services.Application;
using Shapers.Services.Contracts;
using Shapers.Services.Infrastructure;

namespace Shapers.Services.Api;

public sealed class ServicesModule : IModule
{
    /// <summary>Answering from an email: authorised by the signed link, not by a sign-in, so it needs no CSRF header.</summary>
    public const string AnswerPath = "/api/services/answer";

    private static readonly string[] Any = [ServicesPermissions.PlansEdit, ServicesPermissions.Schedule, ServicesPermissions.SongsEdit];

    public string Name => "services";

    public void AddServices(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment) =>
        services.AddServicesInfrastructure(configuration);

    public Task InitialiseAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.InitialiseServicesAsync(cancellationToken);

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/api/admin/services").WithTags("Services admin").RequireAnyPermission(Any);

        var teams = admin.MapGroup("/teams");
        teams.MapGet("/", (bool? includeArchived, TeamService s, CancellationToken ct) => s.ListAsync(includeArchived ?? false, ct)).WithName("ListTeams");
        teams.MapGet("/{id:guid}", async (Guid id, TeamService s, CancellationToken ct) => (await s.GetAsync(id, ct)).ToHttp()).WithName("GetTeam");
        teams.MapPost("/", async (SaveTeamRequest r, TeamService s, CancellationToken ct) => (await s.CreateAsync(r, ct)).ToHttp()).WithName("CreateTeam");
        teams.MapPut("/{id:guid}", async (Guid id, SaveTeamRequest r, TeamService s, CancellationToken ct) => (await s.UpdateAsync(id, r, ct)).ToHttp()).WithName("UpdateTeam");
        teams.MapPost("/{id:guid}/archive", async (Guid id, TeamService s, CancellationToken ct) => (await s.ArchiveAsync(id, true, ct)).ToHttp()).WithName("ArchiveTeam");
        teams.MapPost("/{id:guid}/restore", async (Guid id, TeamService s, CancellationToken ct) => (await s.ArchiveAsync(id, false, ct)).ToHttp()).WithName("RestoreTeam");
        teams.MapPost("/{id:guid}/positions", async (Guid id, SavePositionRequest r, TeamService s, CancellationToken ct) => (await s.AddPositionAsync(id, r, ct)).ToHttp())
            .WithName("AddPosition");
        teams.MapPut("/{id:guid}/positions/{positionId:guid}", async (Guid id, Guid positionId, SavePositionRequest r, TeamService s, CancellationToken ct) =>
            (await s.UpdatePositionAsync(id, positionId, r, ct)).ToHttp()).WithName("UpdatePosition");
        teams.MapDelete("/{id:guid}/positions/{positionId:guid}", async (Guid id, Guid positionId, TeamService s, CancellationToken ct) =>
            (await s.ArchivePositionAsync(id, positionId, ct)).ToHttp()).WithName("ArchivePosition");
        teams.MapPut("/{id:guid}/members", async (Guid id, SaveMemberRequest r, TeamService s, CancellationToken ct) => (await s.SaveMemberAsync(id, r, ct)).ToHttp())
            .WithName("SaveTeamMember");
        teams.MapDelete("/{id:guid}/members/{personId:guid}", async (Guid id, Guid personId, TeamService s, CancellationToken ct) =>
            (await s.RemoveMemberAsync(id, personId, ct)).ToHttp()).WithName("RemoveTeamMember");

        var types = admin.MapGroup("/types");
        types.MapGet("/", (PlanService s, CancellationToken ct) => s.TypesAsync(ct)).WithName("ListServiceTypes");
        types.MapPost("/", async (SaveServiceTypeRequest r, PlanService s, CancellationToken ct) => (await s.CreateTypeAsync(r, ct)).ToHttp()).WithName("CreateServiceType");
        types.MapPut("/{id:guid}", async (Guid id, SaveServiceTypeRequest r, PlanService s, CancellationToken ct) => (await s.UpdateTypeAsync(id, r, ct)).ToHttp())
            .WithName("UpdateServiceType");
        types.MapDelete("/{id:guid}", async (Guid id, PlanService s, CancellationToken ct) => (await s.ArchiveTypeAsync(id, ct)).ToHttp()).WithName("ArchiveServiceType");

        var plans = admin.MapGroup("/plans");
        plans.MapGet("/", (DateOnly? from, DateOnly? to, PlanService s, CancellationToken ct) => s.ListAsync(from, to, ct)).WithName("ListPlans");
        plans.MapGet("/{id:guid}", async (Guid id, PlanService s, CancellationToken ct) => (await s.GetAsync(id, ct)).ToHttp()).WithName("GetPlan");
        plans.MapPost("/", async (CreatePlanRequest r, PlanService s, CancellationToken ct) => (await s.CreateAsync(r, ct)).ToHttp()).WithName("CreatePlan");
        plans.MapPut("/{id:guid}", async (Guid id, SavePlanDetailsRequest r, PlanService s, CancellationToken ct) => (await s.UpdateDetailsAsync(id, r, ct)).ToHttp())
            .WithName("UpdatePlan");
        plans.MapPut("/{id:guid}/items", async (Guid id, SavePlanItemsRequest r, PlanService s, CancellationToken ct) => (await s.SetItemsAsync(id, r, ct)).ToHttp())
            .WithName("SetPlanItems");
        plans.MapPut("/{id:guid}/needs", async (Guid id, SavePlanNeedsRequest r, PlanService s, CancellationToken ct) => (await s.SetNeedsAsync(id, r, ct)).ToHttp())
            .WithName("SetPlanNeeds");
        plans.MapDelete("/{id:guid}", async (Guid id, PlanService s, CancellationToken ct) => (await s.DeleteAsync(id, ct)).ToHttp()).WithName("DeletePlan");
        plans.MapGet("/{id:guid}/candidates", async (Guid id, Guid positionId, ScheduleService s, CancellationToken ct) => (await s.CandidatesAsync(id, positionId, ct)).ToHttp())
            .WithName("PlanCandidates");
        plans.MapPost("/{id:guid}/assignments", async (Guid id, AssignRequest r, ScheduleService s, CancellationToken ct) => (await s.AssignAsync(id, r, ct)).ToHttp())
            .WithName("Schedule");
        plans.MapPost("/{id:guid}/live/{action}", async (Guid id, string action, GoLiveRequest r, LiveService s, CancellationToken ct) =>
            (await s.ControlAsync(id, action, r, ct)).ToHttp()).WithName("ControlLive");
        admin.MapDelete("/assignments/{id:guid}", async (Guid id, ScheduleService s, CancellationToken ct) => (await s.RemoveAsync(id, ct)).ToHttp()).WithName("Unschedule");
        admin.MapGet("/matrix", (DateOnly? from, int? weeks, ScheduleService s, CancellationToken ct) => s.MatrixAsync(from, weeks ?? 6, ct)).WithName("ServingMatrix");

        var songs = admin.MapGroup("/songs");
        songs.MapGet("/", async (string? q, bool? includeArchived, SongService s, CancellationToken ct) => (await s.ListAsync(q, includeArchived ?? false, ct)).ToHttp())
            .WithName("ListSongs");
        songs.MapGet("/report", async (DateOnly from, DateOnly to, SongService s, CancellationToken ct) => (await s.ReportAsync(from, to, ct)).ToHttp()).WithName("SongUsageReport");
        songs.MapGet("/{id:guid}", async (Guid id, SongService s, CancellationToken ct) => (await s.GetAsync(id, ct)).ToHttp()).WithName("GetSong");
        songs.MapPost("/", async (SaveSongRequest r, SongService s, CancellationToken ct) => (await s.CreateAsync(r, ct)).ToHttp()).WithName("CreateSong");
        songs.MapPut("/{id:guid}", async (Guid id, SaveSongRequest r, SongService s, CancellationToken ct) => (await s.UpdateAsync(id, r, ct)).ToHttp()).WithName("UpdateSong");
        songs.MapPost("/{id:guid}/archive", async (Guid id, SongService s, CancellationToken ct) => (await s.ArchiveAsync(id, true, ct)).ToHttp()).WithName("ArchiveSong");
        songs.MapPost("/{id:guid}/restore", async (Guid id, SongService s, CancellationToken ct) => (await s.ArchiveAsync(id, false, ct)).ToHttp()).WithName("RestoreSong");

        // Anyone serving on a plan (and staff): the run sheet and what to rehearse.
        var shared = endpoints.MapGroup("/api/services").WithTags("Serving").RequireAuthorization();
        shared.MapGet("/plans/{id:guid}/live", async (Guid id, LiveService s, CancellationToken ct) => (await s.GetAsync(id, ct)).ToHttp()).WithName("FollowLive");
        shared.MapGet("/plans/{id:guid}/rehearse", async (Guid id, MyServingService s, CancellationToken ct) => (await s.RehearseAsync(id, ct)).ToHttp()).WithName("Rehearse");

        var me = endpoints.MapGroup("/api/me/serving").WithTags("Serving").RequireAuthorization();
        me.MapGet("/", async (MyServingService s, CancellationToken ct) => (await s.ScheduleAsync(ct)).ToHttp()).WithName("MySchedule");
        me.MapPost("/{id:guid}/answer", async (Guid id, ServingAnswerRequest r, MyServingService s, CancellationToken ct) => (await s.AnswerAsync(id, r, ct)).ToHttp())
            .WithName("AnswerServing");
        me.MapGet("/blockouts", async (MyServingService s, CancellationToken ct) => (await s.BlockoutsAsync(ct)).ToHttp()).WithName("MyBlockouts");
        me.MapPost("/blockouts", async (SaveBlockoutRequest r, MyServingService s, CancellationToken ct) => (await s.AddBlockoutAsync(r, ct)).ToHttp()).WithName("AddBlockout");
        me.MapDelete("/blockouts/{id:guid}", async (Guid id, MyServingService s, CancellationToken ct) => (await s.DeleteBlockoutAsync(id, ct)).ToHttp()).WithName("DeleteBlockout");

        // From the email: a page with Yes and No buttons (a GET never changes anything, so link checkers can't answer).
        endpoints.MapGet(AnswerPath, async (string? token, AnswerByLinkService s, CancellationToken ct) =>
            {
                var found = await s.FindAsync(token, ct);
                if (found.IsFailure)
                {
                    return Page("Link expired", found.Error!.Message, null, 400);
                }

                var a = found.Value.Assignment;
                var form = $"""
                    <form method="post" action="{AnswerPath}">
                      <input type="hidden" name="token" value="{WebUtility.HtmlEncode(token)}">
                      <p><button name="answer" value="yes" style="{Button(true)}">Yes, I can serve</button></p>
                      <p><label>If you can't, a short reason helps (optional)<br><input name="reason" maxlength="300" style="width:100%;padding:10px;margin-top:6px;border:1px solid #ccc;border-radius:10px"></label></p>
                      <p><button name="answer" value="no" style="{Button(false)}">No, I can't this time</button></p>
                    </form>
                    """;
                return Page($"{a.Position}, {Day(a.Date)}", $"{a.PlanTitle} at {a.StartTime:HH\\:mm}, {a.Team}. {Status(a.Status)}", form, 200);
            })
            .AllowAnonymous()
            .ExcludeFromDescription();
        endpoints.MapPost(AnswerPath, async (HttpRequest request, AnswerByLinkService s, CancellationToken ct) =>
            {
                var form = await request.ReadFormAsync(ct);
                var accept = form["answer"] == "yes";
                var result = await s.AnswerAsync(form["token"], accept, form["reason"], ct);
                return result.IsFailure
                    ? Page("That didn't work", result.Error!.Message, null, 400)
                    : Page(accept ? "Thank you!" : "Thanks for letting us know", accept
                        ? $"You're serving as {result.Value.Position} on {Day(result.Value.Date)}. You can see the plan in the Shapers app."
                        : "The team will find someone else. You can change your answer from the same link.", null, 200);
            })
            .AllowAnonymous()
            .DisableAntiforgery()
            .ExcludeFromDescription();
    }

    private static string Day(DateOnly date) => date.ToString("dddd d MMMM", CultureInfo.GetCultureInfo("en-ZA"));

    private static string Status(Shapers.Services.Domain.AssignmentStatus status) => status switch
    {
        Shapers.Services.Domain.AssignmentStatus.Accepted => "You've said yes.",
        Shapers.Services.Domain.AssignmentStatus.Declined => "You've said you can't.",
        _ => "Can you make it?",
    };

    private static string Button(bool primary) =>
        $"display:block;width:100%;padding:14px;border-radius:999px;border:0;font-size:16px;font-weight:600;cursor:pointer;{(primary ? "background:#3F3230;color:#EBE6E4" : "background:#EBE6E4;color:#3A2E2B")}";

    private static IResult Page(string heading, string message, string? body, int status) =>
        Results.Content(
            "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">"
            + $"<title>{WebUtility.HtmlEncode(heading)} · Shapers Church</title></head>"
            + "<body style=\"font-family:-apple-system,Segoe UI,Roboto,sans-serif;max-width:480px;margin:10vh auto;padding:0 16px;line-height:1.5;color:#3A2E2B;background:#EBE6E4\">"
            + "<p style=\"letter-spacing:.12em;text-transform:uppercase;font-size:12px\">Shapers Church · Serving</p>"
            + $"<h1 style=\"font-size:24px\">{WebUtility.HtmlEncode(heading)}</h1><p>{WebUtility.HtmlEncode(message)}</p>{body}</body></html>",
            "text/html; charset=utf-8",
            statusCode: status);
}
