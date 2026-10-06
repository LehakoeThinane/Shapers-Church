using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Shapers.Media.Application;

namespace Shapers.Media.Infrastructure;

/// <summary>
/// Google OAuth and the YouTube Data API for captions. Scope youtube.force-ssl is the one YouTube requires to
/// download caption tracks; the church can remove access at any time from the Google account.
/// </summary>
internal sealed class YouTubeCaptionsClient(HttpClient http, IOptions<MediaOptions> options) : IYouTubeCaptions
{
    public const string Scope = "https://www.googleapis.com/auth/youtube.force-ssl";

    private MediaOptions.YouTubeOptions YouTube => options.Value.YouTube;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(YouTube.OAuthClientId) && !string.IsNullOrWhiteSpace(YouTube.OAuthClientSecret);

    public string AuthorizeUrl(string state, string redirectUri) =>
        "https://accounts.google.com/o/oauth2/v2/auth"
        + $"?client_id={Uri.EscapeDataString(YouTube.OAuthClientId!)}"
        + $"&redirect_uri={Uri.EscapeDataString(redirectUri)}"
        + "&response_type=code"
        + $"&scope={Uri.EscapeDataString(Scope)}"
        + "&access_type=offline&prompt=consent&include_granted_scopes=true"
        + $"&state={Uri.EscapeDataString(state)}";

    public async Task<string> ExchangeCodeAsync(string code, string redirectUri, CancellationToken cancellationToken)
    {
        var token = await TokenAsync(new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = YouTube.OAuthClientId!,
            ["client_secret"] = YouTube.OAuthClientSecret!,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code",
        }, cancellationToken);
        return token.RefreshToken ?? throw new YouTubeException("Google didn't allow ongoing access. Remove Shapers from the Google account's third-party access and connect again.");
    }

    public async Task<string> AccessTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var token = await TokenAsync(new Dictionary<string, string>
        {
            ["refresh_token"] = refreshToken,
            ["client_id"] = YouTube.OAuthClientId!,
            ["client_secret"] = YouTube.OAuthClientSecret!,
            ["grant_type"] = "refresh_token",
        }, cancellationToken);
        return token.AccessToken ?? throw new YouTubeException("Google didn't return an access token.");
    }

    public async Task<YouTubeChannel> MyChannelAsync(string accessToken, CancellationToken cancellationToken)
    {
        var list = await GetAsync<ChannelList>("https://www.googleapis.com/youtube/v3/channels?part=snippet&mine=true", accessToken, cancellationToken);
        var channel = list.Items?.FirstOrDefault() ?? throw new YouTubeException("That Google account has no YouTube channel. Sign in with the account that owns the church's channel.");
        return new YouTubeChannel(channel.Id, channel.Snippet?.Title ?? channel.Id);
    }

    public async Task<IReadOnlyList<CaptionTrack>> ListCaptionsAsync(string accessToken, string videoId, CancellationToken cancellationToken)
    {
        var list = await GetAsync<CaptionList>($"https://www.googleapis.com/youtube/v3/captions?part=snippet&videoId={Uri.EscapeDataString(videoId)}", accessToken, cancellationToken);
        return (list.Items ?? [])
            .Where(i => i.Snippet is not null)
            .Select(i => new CaptionTrack(i.Id, i.Snippet!.Language ?? string.Empty, i.Snippet.TrackKind ?? "standard", i.Snippet.IsDraft ?? false))
            .ToList();
    }

    public async Task<string> DownloadSrtAsync(string accessToken, string trackId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://www.googleapis.com/youtube/v3/captions/{Uri.EscapeDataString(trackId)}?tfmt=srt");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await SendAsync(request, cancellationToken);
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task RevokeAsync(string refreshToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/revoke")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = refreshToken }),
        };
        using var _ = await SendAsync(request, cancellationToken);
    }

    private async Task<TokenResponse> TokenAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token") { Content = new FormUrlEncodedContent(form) };
        using var response = await SendAsync(request, cancellationToken);
        return await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken) ?? throw new YouTubeException("Google gave an empty answer.");
    }

    private async Task<T> GetAsync<T>(string url, string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await SendAsync(request, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken) ?? throw new YouTubeException("YouTube gave an empty answer.");
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new YouTubeException("YouTube couldn't be reached. Try again later.", ex);
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        var status = (int)response.StatusCode;
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.Dispose();
        var message = status switch
        {
            400 or 401 => "Google no longer accepts the connection. Connect the church's channel again.",
            403 when body.Contains("quota", StringComparison.OrdinalIgnoreCase) => "YouTube's daily limit has been reached. The rest will follow tomorrow.",
            403 => "YouTube only gives captions to the channel's owner. Connect with the account that owns the church's channel.",
            404 => "YouTube couldn't find that video or caption track.",
            _ => $"YouTube returned an error ({status}).",
        };
        throw new YouTubeException(message, new HttpRequestException(body.Length > 500 ? body[..500] : body));
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken);

    private sealed record ChannelList([property: JsonPropertyName("items")] List<Channel>? Items);

    private sealed record Channel([property: JsonPropertyName("id")] string Id, [property: JsonPropertyName("snippet")] ChannelSnippet? Snippet);

    private sealed record ChannelSnippet([property: JsonPropertyName("title")] string? Title);

    private sealed record CaptionList([property: JsonPropertyName("items")] List<Caption>? Items);

    private sealed record Caption([property: JsonPropertyName("id")] string Id, [property: JsonPropertyName("snippet")] CaptionSnippet? Snippet);

    private sealed record CaptionSnippet(
        [property: JsonPropertyName("language")] string? Language,
        [property: JsonPropertyName("trackKind")] string? TrackKind,
        [property: JsonPropertyName("isDraft")] bool? IsDraft);
}

/// <summary>ASP.NET Core data protection: the same keys that protect sign-in cookies, kept on persistent storage.</summary>
internal sealed class DataProtectionTokenProtector(IDataProtectionProvider provider) : ITokenProtector
{
    private readonly IDataProtector _tokens = provider.CreateProtector("Shapers.Media.YouTube.RefreshToken");
    private readonly ITimeLimitedDataProtector _state = provider.CreateProtector("Shapers.Media.YouTube.OAuthState").ToTimeLimitedDataProtector();

    public string Protect(string value) => _tokens.Protect(value);

    public string? Unprotect(string value)
    {
        try
        {
            return _tokens.Unprotect(value);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }

    public string ProtectFor(string value, TimeSpan lifetime) => _state.Protect(value, lifetime);

    public string? UnprotectTimed(string value)
    {
        try
        {
            return _state.Unprotect(value);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }
}
