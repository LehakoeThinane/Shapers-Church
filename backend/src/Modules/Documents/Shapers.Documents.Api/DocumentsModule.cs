using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shapers.Documents.Application;
using Shapers.Documents.Contracts;
using Shapers.Documents.Domain;
using Shapers.Documents.Infrastructure;
using Shapers.Platform.Authorization;
using Shapers.Platform.Modules;
using Shapers.Platform.Web;

namespace Shapers.Documents.Api;

public sealed class DocumentsModule : IModule
{
    /// <summary>Shown in the browser (preview). Anything else downloads, so an uploaded web page can't run as the API's site.</summary>
    private static readonly HashSet<string> PreviewTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf", "image/png", "image/jpeg", "image/gif", "image/webp", "text/plain",
        "audio/mpeg", "audio/mp4", "audio/x-m4a", "audio/wav", "video/mp4", "video/webm",
    };

    public string Name => "documents";

    public void AddServices(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment) =>
        services.AddDocumentsInfrastructure(configuration);

    public Task InitialiseAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.InitialiseDocumentsAsync(cancellationToken);

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // Who may see or change what is decided per library inside the service; this only keeps out everyone else.
        var docs = endpoints.MapGroup("/api/admin/documents").WithTags("Documents").RequireAuthorization()
            .RequireAnyPermission(DocumentsPermissions.View, DocumentsPermissions.Edit, DocumentsPermissions.Manage);

        docs.MapGet("/libraries", (DocumentsService s, CancellationToken ct) => s.LibrariesAsync(ct)).WithName("ListLibraries");
        docs.MapPost("/libraries", async (SaveLibraryRequest r, DocumentsService s, CancellationToken ct) => (await s.CreateLibraryAsync(r, ct)).ToHttp())
            .WithName("CreateLibrary");
        docs.MapPut("/libraries/{id:guid}", async (Guid id, SaveLibraryRequest r, DocumentsService s, CancellationToken ct) => (await s.UpdateLibraryAsync(id, r, ct)).ToHttp())
            .WithName("UpdateLibrary");
        docs.MapDelete("/libraries/{id:guid}", async (Guid id, DocumentsService s, CancellationToken ct) => (await s.DeleteLibraryAsync(id, ct)).ToHttp())
            .WithName("DeleteLibrary");
        docs.MapGet("/libraries/{id:guid}/members", async (Guid id, DocumentsService s, CancellationToken ct) => (await s.MembersAsync(id, ct)).ToHttp())
            .WithName("LibraryMembers");
        docs.MapPut("/libraries/{id:guid}/members", async (Guid id, List<MemberInput> members, DocumentsService s, CancellationToken ct) =>
                (await s.SetMembersAsync(id, members, ct)).ToHttp())
            .WithName("SetLibraryMembers");

        docs.MapGet("/libraries/{id:guid}/folder", async (Guid id, Guid? folderId, DocumentsService s, CancellationToken ct) => (await s.FolderAsync(id, folderId, ct)).ToHttp())
            .WithName("DocumentsFolder");
        docs.MapGet("/search", (string? q, DocumentsService s, CancellationToken ct) => s.SearchAsync(q, ct)).WithName("SearchDocuments");
        docs.MapGet("/recent", (DocumentsService s, CancellationToken ct) => s.RecentAsync(ct)).WithName("RecentDocuments");

        docs.MapPost("/folders", async (CreateFolderRequest r, DocumentsService s, CancellationToken ct) => (await s.CreateFolderAsync(r, ct)).ToHttp())
            .WithName("CreateDocumentFolder");
        docs.MapPost("/folders/{id:guid}/rename", async (Guid id, RenameRequest r, DocumentsService s, CancellationToken ct) => (await s.RenameFolderAsync(id, r, ct)).ToHttp())
            .WithName("RenameDocumentFolder");
        docs.MapPost("/folders/{id:guid}/move", async (Guid id, MoveRequest r, DocumentsService s, CancellationToken ct) => (await s.MoveFolderAsync(id, r, ct)).ToHttp())
            .WithName("MoveDocumentFolder");
        docs.MapDelete("/folders/{id:guid}", async (Guid id, DocumentsService s, CancellationToken ct) => (await s.DeleteFolderAsync(id, ct)).ToHttp())
            .WithName("DeleteDocumentFolder");

        // Multipart upload through the API (files are private; there's no direct-to-storage link to hand out).
        docs.MapPost("/libraries/{id:guid}/files", (Guid id, Guid? folderId, HttpContext http, DocumentsService s, CancellationToken ct) =>
                WithUploadAsync(http, async file =>
                {
                    await using var content = file.OpenReadStream();
                    return (await s.UploadAsync(id, folderId, file.FileName, file.ContentType, file.Length, content, ct)).ToHttp();
                }, ct))
            .WithName("UploadDocument")
            .DisableAntiforgery()
            .Accepts<IFormFile>("multipart/form-data");
        docs.MapPost("/files/{id:guid}/versions", (Guid id, HttpContext http, DocumentsService s, CancellationToken ct) =>
                WithUploadAsync(http, async file =>
                {
                    await using var content = file.OpenReadStream();
                    return (await s.UploadVersionAsync(id, file.ContentType, file.Length, content, ct)).ToHttp();
                }, ct))
            .WithName("UploadDocumentVersion")
            .DisableAntiforgery()
            .Accepts<IFormFile>("multipart/form-data");
        docs.MapGet("/files/{id:guid}", async (Guid id, DocumentsService s, CancellationToken ct) => (await s.DetailAsync(id, ct)).ToHttp())
            .WithName("GetDocument");

        docs.MapGet("/files/{id:guid}/content", async (Guid id, int? version, bool? download, HttpContext http, DocumentsService s, CancellationToken ct) =>
            {
                var result = await s.OpenAsync(id, version, ct);
                if (result.IsFailure)
                {
                    return result.ToHttp();
                }

                var file = result.Value;
                var inline = download != true && PreviewTypes.Contains(file.ContentType);
                http.Response.Headers.XContentTypeOptions = "nosniff";
                http.Response.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'; img-src 'self'; media-src 'self'; style-src 'unsafe-inline'";
                http.Response.Headers.CacheControl = "private, no-store";
                var disposition = new System.Net.Mime.ContentDisposition { Inline = inline, FileName = AsciiName(file.FileName) };
                http.Response.Headers.ContentDisposition = $"{disposition}; filename*=UTF-8''{Uri.EscapeDataString(file.FileName)}";
                return Results.Stream(file.Content, inline ? file.ContentType : "application/octet-stream", enableRangeProcessing: true);
            })
            .WithName("OpenDocument")
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream");
        docs.MapGet("/files/{id:guid}/versions", async (Guid id, DocumentsService s, CancellationToken ct) => (await s.VersionsAsync(id, ct)).ToHttp())
            .WithName("DocumentVersions");
        docs.MapPost("/files/{id:guid}/versions/{number:int}/restore", async (Guid id, int number, DocumentsService s, CancellationToken ct) =>
                (await s.RestoreVersionAsync(id, number, ct)).ToHttp())
            .WithName("RestoreDocumentVersion");
        docs.MapPost("/files/{id:guid}/rename", async (Guid id, RenameRequest r, DocumentsService s, CancellationToken ct) => (await s.RenameDocumentAsync(id, r, ct)).ToHttp())
            .WithName("RenameDocument");
        docs.MapPost("/files/{id:guid}/move", async (Guid id, MoveRequest r, DocumentsService s, CancellationToken ct) => (await s.MoveDocumentAsync(id, r, ct)).ToHttp())
            .WithName("MoveDocument");
        docs.MapDelete("/files/{id:guid}", async (Guid id, DocumentsService s, CancellationToken ct) => (await s.DeleteDocumentAsync(id, ct)).ToHttp())
            .WithName("DeleteDocument");

        docs.MapGet("/recycle-bin", (DocumentsService s, CancellationToken ct) => s.BinAsync(ct)).WithName("DocumentsRecycleBin");
        docs.MapPost("/recycle-bin/{kind}/{id:guid}/restore", async (string kind, Guid id, DocumentsService s, CancellationToken ct) =>
                (await s.RestoreAsync(kind, id, ct)).ToHttp())
            .WithName("RestoreFromRecycleBin");
    }

    /// <summary>Reads one uploaded file from a form (up to the document size limit) and hands it on.</summary>
    private static async Task<IResult> WithUploadAsync(HttpContext http, Func<IFormFile, Task<IResult>> handle, CancellationToken cancellationToken)
    {
        if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
        {
            limit.MaxRequestBodySize = Document.MaxBytes + (1024 * 1024);
        }

        if (!http.Request.HasFormContentType)
        {
            return Results.Problem("Send the file as a form upload.", statusCode: StatusCodes.Status400BadRequest);
        }

        var form = await http.Request.ReadFormAsync(cancellationToken);
        return form.Files is [var file]
            ? await handle(file)
            : Results.Problem("Send exactly one file.", statusCode: StatusCodes.Status400BadRequest);
    }

    /// <summary>A plain-ASCII fallback for browsers that ignore filename*.</summary>
    private static string AsciiName(string name)
    {
        var ascii = new string(name.Select(c => c is >= ' ' and < (char)127 and not '"' and not '\\' ? c : '_').ToArray());
        return string.IsNullOrWhiteSpace(ascii) ? "document" : ascii;
    }
}
