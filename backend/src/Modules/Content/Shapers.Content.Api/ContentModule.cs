using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shapers.Content.Application;
using Shapers.Content.Contracts;
using Shapers.Content.Domain;
using Shapers.Content.Infrastructure;
using Shapers.Platform.Authorization;
using Shapers.Platform.Modules;
using Shapers.Platform.Web;

namespace Shapers.Content.Api;

public sealed class ContentModule : IModule
{
    public string Name => "content";

    public void AddServices(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment) =>
        services.AddContentInfrastructure(configuration);

    public Task InitialiseAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.InitialiseContentAsync(cancellationToken);

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // Public: read by the app and by the website's build.
        var content = endpoints.MapGroup("/api/content").WithTags("Content").AllowAnonymous();
        content.MapGet("/menu", (PublicContentService s, CancellationToken ct) => s.MenuAsync(ct)).WithName("SiteMenu");
        content.MapGet("/pages", (PublicContentService s, CancellationToken ct) => s.PagesAsync(ct)).WithName("ListPages");
        content.MapGet("/pages/{slug}", async (string slug, string? lang, PublicContentService s, CancellationToken ct) => (await s.PageAsync(slug, lang, ct)).ToHttp()).WithName("GetPage");
        content.MapGet("/posts", (PostKind? kind, int? page, int? pageSize, PublicContentService s, CancellationToken ct) => s.PostsAsync(kind, page ?? 1, pageSize ?? 12, ct))
            .WithName("ListPosts");
        content.MapGet("/posts/{slug}", async (string slug, string? lang, PublicContentService s, CancellationToken ct) => (await s.PostAsync(slug, lang, ct)).ToHttp()).WithName("GetPost");
        content.MapGet("/news", (int? count, PublicContentService s, CancellationToken ct) => s.CurrentNewsAsync(count ?? 3, ct)).WithName("CurrentNews");
        content.MapGet("/redirects", (PublicContentService s, CancellationToken ct) => s.RedirectsAsync(ct)).WithName("LegacyRedirects");

        var admin = endpoints.MapGroup("/api/admin/content").WithTags("Content admin").RequireAuthorization();

        admin.MapPost("/import-wordpress", (WordPressImporter importer, CancellationToken ct) => importer.ImportAsync(ct))
            .WithName("ImportWordPress")
            .RequirePermission(ContentPermissions.Publish);

        admin.MapGet("/translations", (ContentAdminService s, CancellationToken ct) => s.TranslationsAsync(ct))
            .WithName("ListTranslations")
            .RequireAnyPermission(ContentPermissions.Edit, ContentPermissions.TranslationsReview);

        var pages = admin.MapGroup("/pages");
        pages.MapGet("/", (ContentAdminService s, CancellationToken ct) => s.PagesAsync(ct)).WithName("AdminListPages").RequirePermission(ContentPermissions.Edit);
        pages.MapGet("/{id:guid}", async (Guid id, ContentAdminService s, CancellationToken ct) => (await s.PageAsync(id, ct)).ToHttp())
            .WithName("AdminGetPage").RequireAnyPermission(ContentPermissions.Edit, ContentPermissions.TranslationsReview);
        pages.MapPost("/", async (SavePageRequest r, ContentAdminService s, CancellationToken ct) => (await s.CreatePageAsync(r, ct)).ToCreated(p => $"/api/admin/content/pages/{p.Page.Id}"))
            .WithName("CreatePage").RequirePermission(ContentPermissions.Edit);
        pages.MapPut("/{id:guid}", async (Guid id, SavePageRequest r, ContentAdminService s, CancellationToken ct) => (await s.UpdatePageAsync(id, r, ct)).ToHttp())
            .WithName("UpdatePage").RequireAnyPermission(ContentPermissions.Edit, ContentPermissions.TranslationsReview);
        pages.MapPost("/{id:guid}/translations", async (Guid id, CreateTranslationRequest r, ContentAdminService s, CancellationToken ct) => (await s.TranslatePageAsync(id, r, ct)).ToHttp())
            .WithName("TranslatePage").RequirePermission(ContentPermissions.Edit);
        pages.MapPost("/{id:guid}/check", async (Guid id, ContentAdminService s, CancellationToken ct) => (await s.CheckPageAsync(id, ct)).ToHttp())
            .WithName("CheckPageTranslation").RequirePermission(ContentPermissions.TranslationsReview);
        pages.MapPost("/{id:guid}/publish", async (Guid id, ContentAdminService s, CancellationToken ct) =>
                (await s.ChangePageAsync(id, "content.page.published", (p, now) => p.Publish(now), ct)).ToHttp())
            .WithName("PublishPage").RequirePermission(ContentPermissions.Publish);
        pages.MapPost("/{id:guid}/schedule", async (Guid id, ScheduleRequest r, ContentAdminService s, CancellationToken ct) =>
                (await s.ChangePageAsync(id, "content.page.scheduled", (p, now) => p.Schedule(r.PublishAt, now), ct)).ToHttp())
            .WithName("SchedulePage").RequirePermission(ContentPermissions.Publish);
        pages.MapPost("/{id:guid}/unpublish", async (Guid id, ContentAdminService s, CancellationToken ct) =>
                (await s.ChangePageAsync(id, "content.page.unpublished", (p, now) => p.Unpublish(now), ct)).ToHttp())
            .WithName("UnpublishPage").RequirePermission(ContentPermissions.Publish);
        pages.MapPost("/{id:guid}/archive", async (Guid id, ContentAdminService s, CancellationToken ct) =>
                (await s.ChangePageAsync(id, "content.page.archived", (p, now) => p.Archive(now), ct)).ToHttp())
            .WithName("ArchivePage").RequirePermission(ContentPermissions.Publish);

        var posts = admin.MapGroup("/posts");
        posts.MapGet("/", (PostKind? kind, ContentAdminService s, CancellationToken ct) => s.PostsAsync(kind, ct)).WithName("AdminListPosts").RequirePermission(ContentPermissions.Edit);
        posts.MapGet("/{id:guid}", async (Guid id, ContentAdminService s, CancellationToken ct) => (await s.PostAsync(id, ct)).ToHttp())
            .WithName("AdminGetPost").RequireAnyPermission(ContentPermissions.Edit, ContentPermissions.TranslationsReview);
        posts.MapPost("/", async (SavePostRequest r, ContentAdminService s, CancellationToken ct) => (await s.CreatePostAsync(r, ct)).ToCreated(p => $"/api/admin/content/posts/{p.Id}"))
            .WithName("CreatePost").RequirePermission(ContentPermissions.Edit);
        posts.MapPut("/{id:guid}", async (Guid id, SavePostRequest r, ContentAdminService s, CancellationToken ct) => (await s.UpdatePostAsync(id, r, ct)).ToHttp())
            .WithName("UpdatePost").RequireAnyPermission(ContentPermissions.Edit, ContentPermissions.TranslationsReview);
        posts.MapPost("/{id:guid}/translations", async (Guid id, CreateTranslationRequest r, ContentAdminService s, CancellationToken ct) => (await s.TranslatePostAsync(id, r, ct)).ToHttp())
            .WithName("TranslatePost").RequirePermission(ContentPermissions.Edit);
        posts.MapPost("/{id:guid}/check", async (Guid id, ContentAdminService s, CancellationToken ct) => (await s.CheckPostAsync(id, ct)).ToHttp())
            .WithName("CheckPostTranslation").RequirePermission(ContentPermissions.TranslationsReview);
        posts.MapPost("/{id:guid}/publish", async (Guid id, ContentAdminService s, CancellationToken ct) =>
                (await s.ChangePostAsync(id, "content.post.published", (p, now) => p.Publish(now), ct)).ToHttp())
            .WithName("PublishPost").RequirePermission(ContentPermissions.Publish);
        posts.MapPost("/{id:guid}/schedule", async (Guid id, ScheduleRequest r, ContentAdminService s, CancellationToken ct) =>
                (await s.ChangePostAsync(id, "content.post.scheduled", (p, now) => p.Schedule(r.PublishAt, now), ct)).ToHttp())
            .WithName("SchedulePost").RequirePermission(ContentPermissions.Publish);
        posts.MapPost("/{id:guid}/unpublish", async (Guid id, ContentAdminService s, CancellationToken ct) =>
                (await s.ChangePostAsync(id, "content.post.unpublished", (p, now) => p.Unpublish(now), ct)).ToHttp())
            .WithName("UnpublishPost").RequirePermission(ContentPermissions.Publish);
        posts.MapPost("/{id:guid}/archive", async (Guid id, ContentAdminService s, CancellationToken ct) =>
                (await s.ChangePostAsync(id, "content.post.archived", (p, now) => p.Archive(now), ct)).ToHttp())
            .WithName("ArchivePost").RequirePermission(ContentPermissions.Publish);
    }
}
