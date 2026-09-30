using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shapers.Communications.Application;

namespace Shapers.Communications.Infrastructure;

/// <summary>
/// Expo's push service (https://docs.expo.dev/push-notifications/sending-notifications/): up to 100 messages per request,
/// one result per message in the same order. It relays to Firebase (Android) and APNs (iPhone).
/// </summary>
internal sealed class ExpoPushSender(HttpClient http, IOptions<PushOptions> options) : IPushSender
{
    private const int MaxPerRequest = 100;

    public async Task<IReadOnlyList<PushResult>> SendAsync(IReadOnlyList<PushMessage> messages, CancellationToken cancellationToken)
    {
        var results = new List<PushResult>(messages.Count);
        foreach (var chunk in messages.Chunk(MaxPerRequest))
        {
            results.AddRange(await SendChunkAsync(chunk, cancellationToken));
        }

        return results;
    }

    private async Task<IReadOnlyList<PushResult>> SendChunkAsync(PushMessage[] chunk, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "push/send")
        {
            Content = JsonContent.Create(chunk.Select(m => new ExpoMessage(m.Token, m.Title, m.Body, new ExpoData(m.Link), "default", "default")).ToArray()),
        };
        if (!string.IsNullOrWhiteSpace(options.Value.AccessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.AccessToken);
        }

        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = $"Expo push returned {(int)response.StatusCode}";
                return chunk.Select(_ => new PushResult(false, null, false, error)).ToList();
            }

            var body = await response.Content.ReadFromJsonAsync<ExpoResponse>(cancellationToken);
            if (body?.Data is not { } tickets || tickets.Length != chunk.Length)
            {
                return chunk.Select(_ => new PushResult(false, null, false, "Unexpected response from Expo push")).ToList();
            }

            return tickets.Select(t => t.Status == "ok"
                    ? new PushResult(true, t.Id, false, null)
                    : new PushResult(false, null, t.Details?.Error == "DeviceNotRegistered", t.Message ?? t.Details?.Error ?? "Push failed"))
                .ToList();
        }
        catch (HttpRequestException ex)
        {
            return chunk.Select(_ => new PushResult(false, null, false, ex.Message)).ToList();
        }
    }

    private sealed record ExpoMessage(
        [property: JsonPropertyName("to")] string To,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("body")] string Body,
        [property: JsonPropertyName("data")] ExpoData Data,
        [property: JsonPropertyName("sound")] string Sound,
        [property: JsonPropertyName("channelId")] string ChannelId);

    private sealed record ExpoData([property: JsonPropertyName("link")] string? Link);

    private sealed record ExpoResponse([property: JsonPropertyName("data")] ExpoTicket[]? Data);

    private sealed record ExpoTicket(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("details")] ExpoDetails? Details);

    private sealed record ExpoDetails([property: JsonPropertyName("error")] string? Error);
}

/// <summary>Writes pushes to the log instead of sending them.</summary>
internal sealed partial class LoggingPushSender(ILogger<LoggingPushSender> logger) : IPushSender
{
    public Task<IReadOnlyList<PushResult>> SendAsync(IReadOnlyList<PushMessage> messages, CancellationToken cancellationToken)
    {
        foreach (var m in messages)
        {
            LogPush(logger, m.Title, m.Body, m.Link);
        }

        return Task.FromResult<IReadOnlyList<PushResult>>(messages.Select(_ => new PushResult(true, "logged", false, null)).ToList());
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Push (not sent): {Title} | {Body} | {Link}")]
    private static partial void LogPush(ILogger logger, string title, string body, string? link);
}
