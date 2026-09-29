using Shapers.Media.Domain;

namespace Shapers.Media.Tests;

public sealed class LivestreamTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 7, 0, 0, TimeSpan.Zero);
    private static readonly ScopePath Church = ScopePath.Parse("shapers");
    private static readonly VideoLink Video = new(VideoProvider.YouTube, "y0jPz7KFw_o");

    private static Livestream Ready()
    {
        var stream = Livestream.Schedule("Sunday service", Church, Now.AddHours(2), Now);
        stream.Update("Sunday service", Now.AddHours(2), Video, "# Notes", null, Now);
        return stream;
    }

    private static ScriptureReference Ref(string text)
    {
        Assert.True(ScriptureReference.TryParse(text, out var reference));
        return reference;
    }

    [Fact]
    public void Going_live_needs_a_video_link_and_raises_an_event_once()
    {
        var stream = Livestream.Schedule("Sunday service", Church, Now, Now);
        Assert.Throws<DomainRuleException>(() => stream.GoLive(Now));

        stream.Update("Sunday service", Now, Video, null, null, Now);
        stream.GoLive(Now);
        stream.GoLive(Now.AddMinutes(1));

        Assert.Equal(LivestreamStatus.Live, stream.Status);
        Assert.Equal(Now, stream.StartedAt);
        Assert.Single(stream.DomainEvents.OfType<LivestreamStarted>());
    }

    [Fact]
    public void Scripture_goes_on_screen_only_while_live_and_clears_when_the_stream_ends()
    {
        var stream = Ready();
        var cue = stream.AddCue(Ref("Isaiah 42:1-4"), "Behold, my servant, whom I uphold", Now);

        Assert.Throws<DomainRuleException>(() => stream.ShowCue(cue.Id, Now));

        stream.GoLive(Now);
        stream.ShowCue(cue.Id, Now.AddMinutes(20));
        Assert.Equal("Isaiah 42:1–4", stream.CurrentCue!.Reference);
        Assert.Equal(Now.AddMinutes(20), cue.ShownAt);

        stream.End(Now.AddHours(2));
        Assert.Null(stream.CurrentCue);
        Assert.Throws<DomainRuleException>(() => stream.AddCue(Ref("John 1:1"), null, Now));
    }

    [Fact]
    public void Cannot_show_a_cue_from_another_service()
    {
        var stream = Ready();
        stream.GoLive(Now);

        Assert.Throws<DomainRuleException>(() => stream.ShowCue(Guid.NewGuid(), Now));
    }

    [Fact]
    public void Removing_the_cue_on_screen_clears_the_screen()
    {
        var stream = Ready();
        var cue = stream.AddCue(Ref("Psalm 23"), null, Now);
        stream.GoLive(Now);
        stream.ShowCue(cue.Id, Now);

        stream.RemoveCue(cue.Id, Now);

        Assert.Null(stream.CurrentCueId);
    }

    [Fact]
    public void Only_scheduled_streams_can_be_cancelled_and_only_live_ones_ended()
    {
        var stream = Ready();
        Assert.Throws<DomainRuleException>(() => stream.End(Now));

        stream.GoLive(Now);
        Assert.Throws<DomainRuleException>(() => stream.Cancel(Now));
    }

    [Fact]
    public void Give_links_must_be_https()
    {
        var stream = Ready();

        Assert.Throws<DomainRuleException>(() => stream.Update("Sunday", Now, Video, null, "http://pay.example.com", Now));
        stream.Update("Sunday", Now, Video, null, "https://pay.yoco.com/shapers-church", Now);
        Assert.Equal("https://pay.yoco.com/shapers-church", stream.GiveUrl);
    }

    [Fact]
    public void A_service_makes_at_most_one_sermon()
    {
        var stream = Ready();
        stream.LinkSermon(Guid.NewGuid(), Now);

        Assert.Throws<DomainRuleException>(() => stream.LinkSermon(Guid.NewGuid(), Now));
    }
}
