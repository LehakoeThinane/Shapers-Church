using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shapers.Content.Contracts;
using Shapers.Media.Contracts;
using Shapers.Platform.Messaging;

namespace Shapers.Content.Application;

public sealed class SiteRebuildOptions
{
    public const string SectionName = "Content:SiteRebuild";

    /// <summary>
    /// Called when something public changes so the static website rebuilds, e.g. GitHub's repository dispatch:
    /// https://api.github.com/repos/{owner}/{repo}/dispatches. Empty: the site only rebuilds on its hourly schedule.
    /// </summary>
    public string? Url { get; set; }

    /// <summary>A token allowed to trigger the build. Kept in Key Vault, never in the repo.</summary>
    public string? Token { get; set; }
}

/// <summary>Asks the website to rebuild after a page, post or sermon goes live. Best effort: the site also rebuilds hourly.</summary>
public sealed partial class RebuildSiteOnPublish(IHttpClientFactory httpClients, IOptions<SiteRebuildOptions> options, ILogger<RebuildSiteOnPublish> logger)
    : IIntegrationEventHandler<ContentPublishedIntegrationEvent>, IIntegrationEventHandler<SermonPublishedIntegrationEvent>
{
    public Task HandleAsync(ContentPublishedIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        TriggerAsync($"{integrationEvent.Kind} {integrationEvent.Slug}", cancellationToken);

    public Task HandleAsync(SermonPublishedIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        TriggerAsync($"sermon {integrationEvent.Slug}", cancellationToken);

    private async Task TriggerAsync(string reason, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Value.Url))
        {
            return;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, options.Value.Url)
            {
                Content = JsonContent.Create(new { event_type = "site-rebuild", client_payload = new { reason } }),
            };
            if (!string.IsNullOrWhiteSpace(options.Value.Token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.Token);
            }

            request.Headers.UserAgent.ParseAdd("ShapersChurch/1.0");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await httpClients.CreateClient("site-rebuild").SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                LogRefused(logger, reason, (int)response.StatusCode);
            }
        }
        catch (HttpRequestException ex)
        {
            LogFailed(logger, reason, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Website rebuild for {Reason} was refused ({Status})")]
    private static partial void LogRefused(ILogger logger, string reason, int status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Website rebuild for {Reason} failed")]
    private static partial void LogFailed(ILogger logger, string reason, Exception exception);
}
