using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shapers.Assist.Application;
using Shapers.Assist.Contracts;
using Shapers.Assist.Infrastructure;
using Shapers.Platform.Authorization;
using Shapers.Platform.Modules;
using Shapers.Platform.Web;

namespace Shapers.Assist.Api;

public sealed class AssistModule : IModule
{
    public string Name => "assist";

    public void AddServices(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment) =>
        services.AddAssistInfrastructure(configuration, environment);

    public Task InitialiseAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.InitialiseAssistAsync(cancellationToken);

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var assist = endpoints.MapGroup("/api/admin/assist").WithTags("AI help").RequireAuthorization();

        // Lets every screen decide whether to show its AI buttons.
        assist.MapGet("/status", (DraftService s, CancellationToken ct) => s.StatusAsync(ct)).WithName("AssistStatus");
        assist.MapGet("/usage", (UsageService s, CancellationToken ct) => s.GetAsync(ct))
            .WithName("AssistUsage")
            .RequirePermission(AssistPermissions.UsageView);

        var drafts = assist.MapGroup("/drafts").RequirePermission(AssistPermissions.DraftsCreate);
        drafts.MapPost("/sermon-lesson", async (SermonDraftRequest request, DraftService s, CancellationToken ct) => (await s.SermonLessonAsync(request, ct)).ToHttp())
            .WithName("DraftSermonLesson");
        drafts.MapPost("/sermon-notes", async (SermonDraftRequest request, DraftService s, CancellationToken ct) => (await s.SermonNotesAsync(request, ct)).ToHttp())
            .WithName("DraftSermonNotes");
        drafts.MapPost("/rewrite", async (RewriteRequest request, DraftService s, CancellationToken ct) => (await s.RewriteAsync(request, ct)).ToHttp())
            .WithName("DraftRewrite");
        drafts.MapGet("/", (string sourceType, Guid sourceId, DraftService s, CancellationToken ct) => s.ForSourceAsync(sourceType, sourceId, ct))
            .WithName("DraftsForSource");
        drafts.MapGet("/{id:guid}", async (Guid id, DraftService s, CancellationToken ct) => (await s.GetAsync(id, ct)).ToHttp())
            .WithName("GetDraft");
        drafts.MapPost("/{id:guid}/accept", async (Guid id, DraftService s, CancellationToken ct) => (await s.AcceptAsync(id, ct)).ToHttp())
            .WithName("AcceptDraft");
        drafts.MapPost("/{id:guid}/discard", async (Guid id, DraftService s, CancellationToken ct) => (await s.DiscardAsync(id, ct)).ToHttp())
            .WithName("DiscardDraft");
    }
}
