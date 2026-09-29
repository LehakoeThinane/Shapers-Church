using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Shapers.Media.Application;

namespace Shapers.Media.Infrastructure;

/// <summary>Read-only YouTube Data API v3 client: lists a channel's uploads (titles, dates, IDs).</summary>
internal sealed class YouTubeClient(HttpClient http, IOptions<MediaOptions> options) : IYouTubeClient
{
    private const int MaxPages = 20;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.YouTube.ApiKey);

    public async Task<IReadOnlyList<YouTubeVideo>> ListChannelVideosAsync(string channelId, CancellationToken cancellationToken)
    {
        var key = Uri.EscapeDataString(options.Value.YouTube.ApiKey!);
        var channel = await http.GetFromJsonAsync<ChannelList>(
            $"channels?part=contentDetails&id={Uri.EscapeDataString(channelId)}&key={key}", cancellationToken);
        var uploads = channel?.Items?.FirstOrDefault()?.ContentDetails?.RelatedPlaylists?.Uploads
            ?? throw new InvalidOperationException($"YouTube channel {channelId} was not found.");

        var videos = new List<YouTubeVideo>();
        string? pageToken = null;
        for (var page = 0; page < MaxPages; page++)
        {
            var url = $"playlistItems?part=snippet,contentDetails&maxResults=50&playlistId={Uri.EscapeDataString(uploads)}&key={key}"
                + (pageToken is null ? string.Empty : $"&pageToken={Uri.EscapeDataString(pageToken)}");
            var list = await http.GetFromJsonAsync<PlaylistItemList>(url, cancellationToken);
            foreach (var item in list?.Items ?? [])
            {
                var id = item.ContentDetails?.VideoId;
                var title = item.Snippet?.Title;
                var published = item.ContentDetails?.VideoPublishedAt ?? item.Snippet?.PublishedAt;
                if (id is null || title is null || published is null || title is "Private video" or "Deleted video")
                {
                    continue;
                }

                videos.Add(new YouTubeVideo(id, title, published.Value, item.Snippet?.Description));
            }

            pageToken = list?.NextPageToken;
            if (pageToken is null)
            {
                break;
            }
        }

        return videos;
    }

    private sealed record ChannelList([property: JsonPropertyName("items")] List<ChannelItem>? Items);

    private sealed record ChannelItem([property: JsonPropertyName("contentDetails")] ChannelContent? ContentDetails);

    private sealed record ChannelContent([property: JsonPropertyName("relatedPlaylists")] RelatedPlaylists? RelatedPlaylists);

    private sealed record RelatedPlaylists([property: JsonPropertyName("uploads")] string? Uploads);

    private sealed record PlaylistItemList(
        [property: JsonPropertyName("items")] List<PlaylistItem>? Items,
        [property: JsonPropertyName("nextPageToken")] string? NextPageToken);

    private sealed record PlaylistItem(
        [property: JsonPropertyName("snippet")] Snippet? Snippet,
        [property: JsonPropertyName("contentDetails")] ItemContent? ContentDetails);

    private sealed record Snippet(
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("publishedAt")] DateTimeOffset? PublishedAt);

    private sealed record ItemContent(
        [property: JsonPropertyName("videoId")] string? VideoId,
        [property: JsonPropertyName("videoPublishedAt")] DateTimeOffset? VideoPublishedAt);
}
