using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shapers.Media.Application;
using Shapers.Media.Contracts;
using Shapers.Platform;
using Shapers.Platform.Authorization;
using Shapers.Platform.Jobs;

namespace Shapers.Media.Infrastructure;

public static class MediaInfrastructure
{
    public static IServiceCollection AddMediaInfrastructure(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddModuleDbContext<MediaDbContext>(configuration, MediaDbContext.SchemaName, typeof(SermonPublishedIntegrationEvent).Assembly);
        services.AddScoped<IMediaDb>(sp => sp.GetRequiredService<MediaDbContext>());
        services.Configure<MediaOptions>(configuration.GetSection(MediaOptions.SectionName));
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.AddSingleton<IPermissionProvider, MediaPermissionProvider>();

        var provider = configuration[$"{StorageOptions.SectionName}:Provider"] ?? "Local";
        if (provider.Equals("Azure", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IFileStorage, AzureBlobFileStorage>();
        }
        else if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
        {
            services.AddSingleton<LocalFileStorage>();
            services.AddSingleton<IFileStorage>(sp => sp.GetRequiredService<LocalFileStorage>());
        }
        else
        {
            throw new InvalidOperationException("Local media storage is for development only. Set Media:Storage:Provider to Azure.");
        }

        services.AddHttpClient<IYouTubeClient, YouTubeClient>(c =>
        {
            c.BaseAddress = new Uri("https://www.googleapis.com/youtube/v3/");
            c.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddScoped<SermonReader>();
        services.AddScoped<SermonAdminService>();
        services.AddScoped<SeriesService>();
        services.AddScoped<SpeakerService>();
        services.AddScoped<UploadService>();
        services.AddScoped<PublicMediaService>();
        services.AddScoped<PlaybackService>();
        services.AddScoped<ScheduledPublisher>();
        services.AddScoped<PodcastFeedBuilder>();
        services.AddScoped<YouTubeImporter>();

        services.AddSingleton(new RecurringJobDefinition("media-publish-scheduled", "* * * * *", (sp, ct) =>
            sp.GetRequiredService<ScheduledPublisher>().PublishDueAsync(ct)));
        services.AddSingleton(new RecurringJobDefinition("media-playback-retention", "45 3 * * *", (sp, ct) =>
            sp.GetRequiredService<ScheduledPublisher>().PurgeOldPlaybackAsync(ct)));
        return services;
    }

    public static async Task InitialiseMediaAsync(this IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<MediaDbContext>().Database.MigrateAsync(cancellationToken);
    }
}

/// <summary>Development only: receives signed uploads and serves files from the local disk, with range requests for audio seeking.</summary>
public static class LocalStorageEndpoints
{
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    public static void MapLocalStorage(this IEndpointRouteBuilder endpoints)
    {
        if (endpoints.ServiceProvider.GetService<LocalFileStorage>() is null)
        {
            return;
        }

        endpoints.MapPut("/api/media/local-upload", async (HttpRequest request, LocalFileStorage storage, string key, string size, string expires, string sig, CancellationToken ct) =>
            {
                if (!LocalFileStorage.Verify(key, size, expires, sig))
                {
                    return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Upload link is invalid or has expired.");
                }

                var path = storage.PathFor(key);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var limit = long.Parse(size, System.Globalization.CultureInfo.InvariantCulture);
                await using (var file = File.Create(path))
                {
                    await request.Body.CopyToAsync(file, ct);
                    if (file.Length > limit)
                    {
                        file.Close();
                        File.Delete(path);
                        return Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: "The file is larger than declared.");
                    }
                }

                return Results.Created();
            })
            .AllowAnonymous()
            .ExcludeFromDescription();

        endpoints.MapGet("/media-files/{**key}", (string key, LocalFileStorage storage) =>
            {
                string path;
                try
                {
                    path = storage.PathFor(key);
                }
                catch (InvalidOperationException)
                {
                    return Results.NotFound();
                }

                if (!File.Exists(path))
                {
                    return Results.NotFound();
                }

                var type = ContentTypes.TryGetContentType(path, out var contentType) ? contentType : "application/octet-stream";
                return Results.File(path, type, enableRangeProcessing: true);
            })
            .AllowAnonymous()
            .ExcludeFromDescription();
    }
}
