using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shapers.Media.Application;
using Shapers.Media.Contracts;
using Shapers.Media.Infrastructure;
using Shapers.Platform.Authorization;
using Shapers.Platform.Modules;
using Shapers.Platform.Web;

namespace Shapers.Media.Api;

public sealed class MediaModule : IModule
{
    public string Name => "media";

    public void AddServices(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddMediaInfrastructure(configuration, environment);

        // One API instance holds every connection for now. Running several needs Azure SignalR Service (AddAzureSignalR).
        services.AddSignalR();
        services.AddSingleton<IChatBroadcaster, SignalRChatBroadcaster>();
    }

    public Task InitialiseAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.InitialiseMediaAsync(cancellationToken);

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        MapPublic(endpoints.MapGroup("/api/media").WithTags("Sermons").AllowAnonymous());
        MapAdmin(endpoints.MapGroup("/api/admin/media").WithTags("Sermons admin").RequireAuthorization());

        var me = endpoints.MapGroup("/api/me/playback").WithTags("Sermons").RequireAuthorization();
        me.MapGet("/", (PlaybackService service, CancellationToken ct) => service.ContinueAsync(ct)).WithName("ContinueListening");
        me.MapPut("/{sermonId:guid}", async (Guid sermonId, PlaybackUpdateRequest request, PlaybackService service, CancellationToken ct) =>
                (await service.UpdateAsync(sermonId, request, ct)).ToHttp())
            .WithName("UpdatePlayback");

        endpoints.MapGet("/podcast.xml", async (HttpRequest request, PodcastFeedBuilder feed, CancellationToken ct) =>
            {
                var url = $"{request.Scheme}://{request.Host}{request.PathBase}/podcast.xml";
                return Results.Content(await feed.BuildAsync(url, ct), "application/rss+xml; charset=utf-8");
            })
            .AllowAnonymous()
            .WithTags("Sermons")
            .WithName("PodcastFeed");

        endpoints.MapLocalStorage();
        MapChat(endpoints);
    }

    private static void MapChat(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHub<LiveChatHub>(LiveChatHub.Path);

        var chat = endpoints.MapGroup("/api/media/live/{livestreamId:guid}/chat").WithTags("Live chat");
        chat.MapGet("/", async (Guid livestreamId, HttpContext http, LiveChatService service, CancellationToken ct) =>
            {
                http.Response.Headers.CacheControl = "no-store";
                return (await service.RoomAsync(livestreamId, ct)).ToHttp();
            })
            .AllowAnonymous()
            .WithName("GetLiveChat");
        chat.MapPost("/", async (Guid livestreamId, PostChatRequest request, LiveChatService service, CancellationToken ct) =>
                (await service.PostAsync(livestreamId, request, ct)).ToHttp())
            .RequireAuthorization()
            .WithName("PostLiveChat");
        chat.MapPost("/{messageId:guid}/report", async (Guid livestreamId, Guid messageId, LiveChatService service, CancellationToken ct) =>
                (await service.ReportAsync(livestreamId, messageId, ct)).ToHttp())
            .RequireAuthorization()
            .WithName("ReportLiveChatMessage");

        var mod = endpoints.MapGroup("/api/admin/media/chat").WithTags("Live chat moderation").RequirePermission(MediaPermissions.ChatModerate);
        mod.MapGet("/", (ChatModerationService service, CancellationToken ct) => service.StreamsAsync(ct)).WithName("ListChatStreams");
        mod.MapGet("/words", (ChatModerationService service, CancellationToken ct) => service.WordsAsync(ct)).WithName("GetChatWordList");
        mod.MapPut("/words", async (WordListDto request, ChatModerationService service, CancellationToken ct) => (await service.SetWordsAsync(request, ct)).ToHttp())
            .WithName("SetChatWordList");
        mod.MapPost("/sanctions/{sanctionId:guid}/lift", async (Guid sanctionId, ChatModerationService service, CancellationToken ct) =>
                (await service.LiftAsync(sanctionId, ct)).ToHttp())
            .WithName("LiftChatSanction");
        mod.MapGet("/{livestreamId:guid}", async (Guid livestreamId, HttpContext http, ChatModerationService service, CancellationToken ct) =>
            {
                http.Response.Headers.CacheControl = "no-store";
                return (await service.RoomAsync(livestreamId, ct)).ToHttp();
            })
            .WithName("ModerateChat");
        mod.MapPost("/{livestreamId:guid}/messages/{messageId:guid}/hide", async (Guid livestreamId, Guid messageId, ChatModerationService service, CancellationToken ct) =>
                (await service.HideAsync(livestreamId, messageId, ct)).ToHttp())
            .WithName("HideChatMessage");
        mod.MapPost("/{livestreamId:guid}/messages/{messageId:guid}/show", async (Guid livestreamId, Guid messageId, ChatModerationService service, CancellationToken ct) =>
                (await service.ShowAsync(livestreamId, messageId, ct)).ToHttp())
            .WithName("ShowChatMessage");
        mod.MapPost("/{livestreamId:guid}/sanctions", async (Guid livestreamId, SanctionRequest request, ChatModerationService service, CancellationToken ct) =>
                (await service.SanctionAsync(livestreamId, request, ct)).ToHttp())
            .WithName("SanctionChatter");
        mod.MapPut("/{livestreamId:guid}/rules", async (Guid livestreamId, ChatRulesRequest request, ChatModerationService service, CancellationToken ct) =>
                (await service.SetRulesAsync(livestreamId, request, ct)).ToHttp())
            .WithName("SetChatRules");
    }

    private static void MapPublic(RouteGroupBuilder media)
    {
        media.MapGet("/sermons", ([AsParameters] SermonSearchQuery query, PublicMediaService service, CancellationToken ct) => service.SearchAsync(query, ct))
            .WithName("SearchSermons");
        media.MapGet("/sermons/{slugOrId}", async (string slugOrId, PublicMediaService service, CancellationToken ct) =>
                (await service.GetAsync(slugOrId, ct)).ToHttp())
            .WithName("GetSermon");
        media.MapGet("/series", (PublicMediaService service, CancellationToken ct) => service.SeriesAsync(ct)).WithName("ListPublicSeries");
        media.MapGet("/series/{slug}", async (string slug, PublicMediaService service, CancellationToken ct) =>
                (await service.SeriesBySlugAsync(slug, ct)).ToHttp())
            .WithName("GetSeries");
        media.MapGet("/speakers", (PublicMediaService service, CancellationToken ct) => service.SpeakersAsync(ct)).WithName("ListPublicSpeakers");

        // Polled by every app on the Live tab during a service: a short shared cache keeps that cheap.
        media.MapGet("/live", async (HttpContext http, LivestreamService service, CancellationToken ct) =>
            {
                http.Response.Headers.CacheControl = "public, max-age=10";
                return TypedResults.Ok(await service.NowAsync(ct));
            })
            .WithName("LiveNow");
    }

    private static void MapAdmin(RouteGroupBuilder admin)
    {
        var sermons = admin.MapGroup("/sermons");
        sermons.MapGet("/", ([AsParameters] AdminSermonQuery query, SermonAdminService service, CancellationToken ct) => service.ListAsync(query, ct))
            .WithName("AdminListSermons")
            .RequirePermission(MediaPermissions.SermonsEdit);
        sermons.MapGet("/{id:guid}", async (Guid id, SermonAdminService service, CancellationToken ct) => (await service.GetAsync(id, ct)).ToHttp())
            .WithName("AdminGetSermon")
            .RequirePermission(MediaPermissions.SermonsEdit);
        sermons.MapPost("/", async (SaveSermonRequest request, SermonAdminService service, CancellationToken ct) =>
                (await service.CreateAsync(request, ct)).ToCreated(s => $"/api/admin/media/sermons/{s.Sermon.Id}"))
            .WithName("CreateSermon")
            .RequirePermission(MediaPermissions.SermonsEdit);
        sermons.MapPut("/{id:guid}", async (Guid id, SaveSermonRequest request, SermonAdminService service, CancellationToken ct) =>
                (await service.UpdateAsync(id, request, ct)).ToHttp())
            .WithName("UpdateSermon")
            .RequirePermission(MediaPermissions.SermonsEdit);
        sermons.MapPut("/{id:guid}/audio", async (Guid id, SetSermonAssetRequest request, SermonAdminService service, CancellationToken ct) =>
                (await service.SetAudioAsync(id, request, ct)).ToHttp())
            .WithName("SetSermonAudio")
            .RequirePermission(MediaPermissions.SermonsEdit);
        sermons.MapPut("/{id:guid}/notes-pdf", async (Guid id, SetSermonAssetRequest request, SermonAdminService service, CancellationToken ct) =>
                (await service.SetNotesPdfAsync(id, request, ct)).ToHttp())
            .WithName("SetSermonNotesPdf")
            .RequirePermission(MediaPermissions.SermonsEdit);
        sermons.MapPost("/{id:guid}/publish", async (Guid id, SermonAdminService service, CancellationToken ct) => (await service.PublishAsync(id, ct)).ToHttp())
            .WithName("PublishSermon")
            .RequirePermission(MediaPermissions.SermonsPublish);
        sermons.MapPost("/{id:guid}/schedule", async (Guid id, ScheduleRequest request, SermonAdminService service, CancellationToken ct) =>
                (await service.ScheduleAsync(id, request, ct)).ToHttp())
            .WithName("ScheduleSermon")
            .RequirePermission(MediaPermissions.SermonsPublish);
        sermons.MapPost("/{id:guid}/unpublish", async (Guid id, SermonAdminService service, CancellationToken ct) => (await service.UnpublishAsync(id, ct)).ToHttp())
            .WithName("UnpublishSermon")
            .RequirePermission(MediaPermissions.SermonsPublish);
        sermons.MapPost("/{id:guid}/archive", async (Guid id, SermonAdminService service, CancellationToken ct) => (await service.ArchiveAsync(id, ct)).ToHttp())
            .WithName("ArchiveSermon")
            .RequirePermission(MediaPermissions.SermonsPublish);
        sermons.MapPost("/{id:guid}/restore", async (Guid id, SermonAdminService service, CancellationToken ct) => (await service.RestoreAsync(id, ct)).ToHttp())
            .WithName("RestoreSermon")
            .RequirePermission(MediaPermissions.SermonsPublish);
        sermons.MapDelete("/{id:guid}", async (Guid id, SermonAdminService service, CancellationToken ct) => (await service.DeleteAsync(id, ct)).ToHttp())
            .WithName("DeleteSermon")
            .RequirePermission(MediaPermissions.SermonsEdit);

        admin.MapGet("/scripture-check", (string? text) => TypedResults.Ok(SermonAdminService.CheckScripture(text)))
            .WithName("CheckScripture")
            .RequirePermission(MediaPermissions.SermonsEdit);
        admin.MapPost("/import/youtube", async (YouTubeImporter importer, CancellationToken ct) => (await importer.ImportAsync(ct)).ToHttp())
            .WithName("ImportFromYouTube")
            .RequirePermission(MediaPermissions.SermonsEdit);

        var series = admin.MapGroup("/series");
        series.MapGet("/", (SeriesService service, CancellationToken ct) => service.ListAsync(ct))
            .WithName("AdminListSeries")
            .RequirePermission(MediaPermissions.SermonsEdit);
        series.MapPost("/", async (SaveSeriesRequest request, SeriesService service, CancellationToken ct) => (await service.CreateAsync(request, ct)).ToHttp())
            .WithName("CreateSeries")
            .RequirePermission(MediaPermissions.SermonsEdit);
        series.MapPut("/{id:guid}", async (Guid id, SaveSeriesRequest request, SeriesService service, CancellationToken ct) =>
                (await service.UpdateAsync(id, request, ct)).ToHttp())
            .WithName("UpdateSeries")
            .RequirePermission(MediaPermissions.SermonsEdit);

        var speakers = admin.MapGroup("/speakers");
        speakers.MapGet("/", (SpeakerService service, bool? includeInactive, CancellationToken ct) => service.ListAsync(includeInactive ?? false, ct))
            .WithName("AdminListSpeakers")
            .RequirePermission(MediaPermissions.SermonsEdit);
        speakers.MapPost("/", async (SaveSpeakerRequest request, SpeakerService service, CancellationToken ct) => (await service.SaveAsync(null, request, ct)).ToHttp())
            .WithName("CreateSpeaker")
            .RequirePermission(MediaPermissions.SpeakersManage);
        speakers.MapPut("/{id:guid}", async (Guid id, SaveSpeakerRequest request, SpeakerService service, CancellationToken ct) =>
                (await service.SaveAsync(id, request, ct)).ToHttp())
            .WithName("UpdateSpeaker")
            .RequirePermission(MediaPermissions.SpeakersManage);

        var live = admin.MapGroup("/livestreams").RequirePermission(MediaPermissions.LivestreamManage);
        live.MapGet("/", (LivestreamService service, CancellationToken ct) => service.ListAsync(ct)).WithName("ListLivestreams");
        live.MapGet("/{id:guid}", async (Guid id, LivestreamService service, CancellationToken ct) => (await service.GetAsync(id, ct)).ToHttp())
            .WithName("GetLivestream");
        live.MapPost("/", async (SaveLivestreamRequest request, LivestreamService service, CancellationToken ct) =>
                (await service.CreateAsync(request, ct)).ToCreated(s => $"/api/admin/media/livestreams/{s.Id}"))
            .WithName("CreateLivestream");
        live.MapPut("/{id:guid}", async (Guid id, SaveLivestreamRequest request, LivestreamService service, CancellationToken ct) =>
                (await service.UpdateAsync(id, request, ct)).ToHttp())
            .WithName("UpdateLivestream");
        live.MapPost("/{id:guid}/go-live", async (Guid id, LivestreamService service, CancellationToken ct) => (await service.GoLiveAsync(id, ct)).ToHttp())
            .WithName("GoLive");
        live.MapPost("/{id:guid}/end", async (Guid id, LivestreamService service, CancellationToken ct) => (await service.EndAsync(id, ct)).ToHttp())
            .WithName("EndLivestream");
        live.MapPost("/{id:guid}/cancel", async (Guid id, LivestreamService service, CancellationToken ct) => (await service.CancelAsync(id, ct)).ToHttp())
            .WithName("CancelLivestream");
        live.MapPost("/{id:guid}/cues", async (Guid id, AddCueRequest request, LivestreamService service, CancellationToken ct) =>
                (await service.AddCueAsync(id, request, ct)).ToHttp())
            .WithName("AddScriptureCue");
        live.MapDelete("/{id:guid}/cues/{cueId:guid}", async (Guid id, Guid cueId, LivestreamService service, CancellationToken ct) =>
                (await service.RemoveCueAsync(id, cueId, ct)).ToHttp())
            .WithName("RemoveScriptureCue");
        live.MapPost("/{id:guid}/on-screen", async (Guid id, ShowCueRequest request, LivestreamService service, CancellationToken ct) =>
                (await service.ShowCueAsync(id, request, ct)).ToHttp())
            .WithName("ShowScriptureCue");
        live.MapPost("/{id:guid}/make-sermon", async (Guid id, LivestreamService service, CancellationToken ct) =>
                (await service.MakeSermonAsync(id, ct)).ToHttp())
            .WithName("MakeSermonFromLivestream");

        var uploads = admin.MapGroup("/uploads");
        uploads.MapPost("/", async (StartUploadRequest request, UploadService service, CancellationToken ct) => (await service.StartAsync(request, ct)).ToHttp())
            .WithName("StartUpload");
        uploads.MapPost("/{id:guid}/complete", async (Guid id, CompleteUploadRequest request, UploadService service, CancellationToken ct) =>
                (await service.CompleteAsync(id, request, ct)).ToHttp())
            .WithName("CompleteUpload");
    }
}
