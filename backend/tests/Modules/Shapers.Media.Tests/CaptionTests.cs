using Shapers.Media.Application;
using Shapers.Media.Domain;

namespace Shapers.Media.Tests;

public sealed class CaptionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Srt_becomes_readable_text_with_paragraphs_at_pauses()
    {
        const string srt = """
            1
            00:00:01,000 --> 00:00:03,000
            Good morning, church.

            2
            00:00:03,200 --> 00:00:05,000
            <i>Turn with me</i> to James 2.

            3
            00:00:05,000 --> 00:00:06,000
            [Music]

            4
            00:00:10,000 --> 00:00:12,000
            Faith without works &amp; words
            Faith without works &amp; words
            """;

        var text = SrtText.ToPlainText(srt);

        Assert.Equal("Good morning, church. Turn with me to James 2.\n\nFaith without works & words", text);
    }

    [Fact]
    public void Srt_handles_windows_line_endings_and_empty_input()
    {
        Assert.Equal("Amen.", SrtText.ToPlainText("1\r\n00:00:01,000 --> 00:00:02,000\r\nAmen.\r\n"));
        Assert.Equal(string.Empty, SrtText.ToPlainText(string.Empty));
    }

    [Fact]
    public void The_churchs_own_english_captions_are_preferred_over_automatic_ones()
    {
        CaptionTrack[] tracks =
        [
            new("asr", "en", "asr", false),
            new("zulu", "zu", "standard", false),
            new("draft", "en", "standard", true),
            new("own", "en-GB", "standard", false),
        ];

        Assert.Equal("own", YouTubeCaptionService.ChooseTrack(tracks)!.Id);
        Assert.Equal("asr", YouTubeCaptionService.ChooseTrack(tracks.Where(t => t.Id != "own"))!.Id);
        Assert.Null(YouTubeCaptionService.ChooseTrack(tracks.Where(t => t.Language == "zu")));
    }

    [Fact]
    public void A_transcript_from_captions_is_ready_and_marked_as_captions()
    {
        var sermon = Sermon.Create("Faith that works", new DateOnly(2026, 10, 4), ScopePath.Parse("shapers"), Now);

        sermon.SetTranscript("Faith without works is dead.", TranscriptSource.Captions, Now);

        Assert.Equal(TranscriptStatus.Ready, sermon.TranscriptStatus);
        Assert.Equal(TranscriptSource.Captions, sermon.TranscriptSource);
    }

    [Fact]
    public void A_connection_needs_a_channel_and_a_token() =>
        Assert.Throws<DomainRuleException>(() => YouTubeConnection.Connect("", "Shapers", "token", Guid.NewGuid(), Now));
}
