using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Shapers.Media.Application;
using Shapers.Media.Domain;

namespace Shapers.IntegrationTests;

public sealed class YouTubeCaptionTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task The_owner_connects_the_channel_once_and_captions_become_transcripts()
    {
        var admin = await api.SignInAdminAsync();
        Assert.False((await (await admin.GetAsync("/api/admin/media/youtube")).ReadAsync<YouTubeConnectionDto>()).Connected);

        // The admin portal opens Google's page; Google sends the owner back with a code and the same state.
        var start = await (await admin.PostAsync("/api/admin/media/youtube/connect", null)).ReadAsync<YouTubeConnectStartDto>();
        var state = Uri.UnescapeDataString(start.AuthorizeUrl.Split("state=")[1]);
        Assert.Contains("redirect_uri=", start.AuthorizeUrl, StringComparison.Ordinal);

        var google = api.Browser();
        var tampered = await google.GetAsync($"/api/media/youtube/callback?code=abc&state={Uri.EscapeDataString(state + "x")}");
        Assert.Equal(HttpStatusCode.BadRequest, tampered.StatusCode);
        var callback = await google.GetAsync($"/api/media/youtube/callback?code=abc&state={Uri.EscapeDataString(state)}");
        Assert.Equal(HttpStatusCode.OK, callback.StatusCode);
        Assert.Contains("YouTube connected", await callback.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var status = await (await admin.GetAsync("/api/admin/media/youtube")).ReadAsync<YouTubeConnectionDto>();
        Assert.True(status.Connected);
        Assert.True(status.IsChurchChannel);
        Assert.Equal("Shapers Church", status.ChannelTitle);

        // A sermon with a video gets its captions on request...
        var withCaptions = await CreateSermonAsync(admin, "Faith that works", "abcdefghijk");
        var updated = await (await admin.PostAsync($"/api/admin/media/sermons/{withCaptions.Sermon.Id}/transcript/captions", null)).ReadAsync<SermonAdminDto>();
        Assert.Equal(TranscriptStatus.Ready, updated.Transcript.Status);
        Assert.Equal(TranscriptSource.Captions, updated.Transcript.Source);
        var transcript = await (await admin.GetAsync($"/api/admin/media/sermons/{withCaptions.Sermon.Id}/transcript")).ReadAsync<TranscriptDto>();
        Assert.Equal("Faith without works is dead.", transcript.Text);

        // ...and a video without captions says so, so the media team can paste or upload instead.
        var without = await CreateSermonAsync(admin, "No captions yet", FakeYouTubeCaptions.NoCaptionsVideo);
        var refused = await admin.PostAsync($"/api/admin/media/sermons/{without.Sermon.Id}/transcript/captions", null);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("media.no_captions", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // The hourly job picks up sermons with a video and no transcript.
        var later = await CreateSermonAsync(admin, "Picked up by the job", "y0jPz7KFw_o");
        await using (var scope = api.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<YouTubeCaptionService>().RunAsync(CancellationToken.None);
        }

        var fromJob = await (await admin.GetAsync($"/api/admin/media/sermons/{later.Sermon.Id}")).ReadAsync<SermonAdminDto>();
        Assert.Equal(TranscriptSource.Captions, fromJob.Transcript.Source);

        (await admin.DeleteAsync("/api/admin/media/youtube")).EnsureSuccessStatusCode();
        Assert.False((await (await admin.GetAsync("/api/admin/media/youtube")).ReadAsync<YouTubeConnectionDto>()).Connected);
    }

    [Fact]
    public async Task Captions_need_a_connected_channel()
    {
        var admin = await api.SignInAdminAsync();
        (await admin.DeleteAsync("/api/admin/media/youtube")).EnsureSuccessStatusCode();
        var sermon = await CreateSermonAsync(admin, "Not connected", "abcdefghij2");

        var refused = await admin.PostAsync($"/api/admin/media/sermons/{sermon.Sermon.Id}/transcript/captions", null);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("media.youtube_not_connected", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    private static async Task<SermonAdminDto> CreateSermonAsync(HttpClient admin, string title, string videoId)
    {
        var speaker = await (await admin.PostJsonAsync("/api/admin/media/speakers", new SaveSpeakerRequest($"Speaker {Guid.NewGuid():N}"[..20], null, null, null, null))).ReadAsync<SpeakerDto>();
        return await (await admin.PostJsonAsync("/api/admin/media/sermons", new SaveSermonRequest(title, new DateOnly(2026, 10, 4), null, null, null, [], [speaker.Id], null, videoId, null)))
            .ReadAsync<SermonAdminDto>();
    }
}
