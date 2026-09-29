using Shapers.Media.Domain;

namespace Shapers.Media.Tests;

public sealed class ScriptureTests
{
    [Theory]
    [InlineData("Psalm 42:1-11", "Psalm 42:1–11")]
    [InlineData("Ps 42: 1-11", "Psalm 42:1–11")]
    [InlineData("psalms 23", "Psalm 23")]
    [InlineData("John 3:16", "John 3:16")]
    [InlineData("Jn 3.16", "John 3:16")]
    [InlineData("Romans 8", "Romans 8")]
    [InlineData("Genesis 1-3", "Genesis 1–3")]
    [InlineData("Isaiah 52:13-53:12", "Isaiah 52:13–53:12")]
    [InlineData("1 Cor 13:4-7", "1 Corinthians 13:4–7")]
    [InlineData("I Corinthians 13", "1 Corinthians 13")]
    [InlineData("First Peter 2:9", "1 Peter 2:9")]
    [InlineData("2 Tim. 3:16", "2 Timothy 3:16")]
    [InlineData("Song of Solomon 2:4", "Song of Songs 2:4")]
    [InlineData("Jude 3", "Jude 1:3")]
    [InlineData("Jude 20-25", "Jude 1:20–25")]
    [InlineData("Philemon 1:6", "Philemon 1:6")]
    [InlineData("Rev 21:1–4", "Revelation 21:1–4")]
    public void Parses_and_formats_common_references(string input, string expected)
    {
        Assert.True(ScriptureReference.TryParse(input, out var reference), input);
        Assert.Equal(expected, reference.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("Hezekiah 3:1")]
    [InlineData("Psalm 151")]
    [InlineData("John 22:1")]
    [InlineData("John 3:16-3")]
    [InlineData("Genesis 5-2")]
    [InlineData("Deep calls unto deep")]
    public void Rejects_invalid_references(string input)
    {
        Assert.False(ScriptureReference.TryParse(input, out _));
    }

    [Fact]
    public void Parses_semicolon_separated_lists_and_reports_the_rest()
    {
        var refs = ScriptureReference.ParseMany("Isaiah 42:1-9; Matthew 12:18-21; Hezekiah 1", out var unrecognised);

        Assert.Equal(["Isaiah 42:1–9", "Matthew 12:18–21"], refs.Select(r => r.ToString()));
        Assert.Equal(["Hezekiah 1"], unrecognised);
    }

    [Theory]
    [InlineData("Psalm 42: 1-11 Deep calls unto deep", "Psalm 42:1–11", "Deep calls unto deep")]
    [InlineData("Isaiah 53:1-12 - The suffering servant", "Isaiah 53:1–12", "The suffering servant")]
    [InlineData("1 Cor 13 | Love never fails", "1 Corinthians 13", "Love never fails")]
    public void Extracts_a_leading_reference_from_existing_youtube_titles(string title, string reference, string rest)
    {
        Assert.True(ScriptureReference.TryExtractFromTitle(title, out var parsed, out var remainder));
        Assert.Equal(reference, parsed.ToString());
        Assert.Equal(rest, remainder);
    }

    [Theory]
    [InlineData("Faith that works")]
    [InlineData("Easter Sunday 2026")]
    public void Leaves_titles_without_a_reference_alone(string title)
    {
        Assert.False(ScriptureReference.TryExtractFromTitle(title, out _, out var remainder));
        Assert.Equal(title, remainder);
    }
}

public sealed class SermonTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 8, 0, 0, TimeSpan.Zero);
    private static readonly ScopePath Church = ScopePath.Parse("shapers");

    private static Sermon Draft() => Sermon.Create("Deep calls unto deep", new DateOnly(2026, 9, 27), Church, Now);

    private static MediaAsset ReadyAudio()
    {
        var asset = MediaAsset.StartUpload(MediaKind.Audio, "sunday.mp3", "audio/mpeg", 28_000_000, null, Now);
        asset.MarkReady(28_000_000, 3_540);
        return asset;
    }

    [Fact]
    public void Slug_includes_the_date_and_survives_title_edits()
    {
        var sermon = Draft();
        Assert.Equal("2026-09-27-deep-calls-unto-deep", sermon.Slug);

        sermon.UpdateDetails("A new title", sermon.PreachedOn, null, null, null, [], Now);
        Assert.Equal("2026-09-27-deep-calls-unto-deep", sermon.Slug);
    }

    [Fact]
    public void Cannot_publish_without_a_speaker_and_media()
    {
        var sermon = Draft();

        var error = Assert.Throws<DomainRuleException>(() => sermon.Publish(Now));
        Assert.Contains("speaker", error.Message, StringComparison.Ordinal);
        Assert.Contains("audio or a video", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Publishing_raises_an_event_once_and_keeps_the_first_publish_time()
    {
        var sermon = Draft();
        sermon.SetSpeakers([Guid.NewGuid()], Now);
        sermon.AttachAudio(ReadyAudio(), Now);

        sermon.Publish(Now);
        sermon.Unpublish(Now.AddHours(1));
        sermon.Publish(Now.AddHours(2));

        Assert.Equal(Now, sermon.PublishedAt);
        Assert.Equal(2, sermon.DomainEvents.OfType<SermonPublished>().Count());
        Assert.Equal(3_540, sermon.AudioDurationSeconds);
    }

    [Fact]
    public void Scheduled_sermons_publish_only_when_due()
    {
        var sermon = Draft();
        sermon.SetSpeakers([Guid.NewGuid()], Now);
        sermon.SetVideo(new VideoLink(VideoProvider.YouTube, "y0jPz7KFw_o"), Now);
        sermon.Schedule(Now.AddDays(1), Now);

        Assert.False(sermon.PublishIfDue(Now.AddHours(23)));
        Assert.Equal(SermonStatus.Scheduled, sermon.Status);
        Assert.True(sermon.PublishIfDue(Now.AddDays(1)));
        Assert.Equal(SermonStatus.Published, sermon.Status);
    }

    [Fact]
    public void Cannot_schedule_in_the_past()
    {
        var sermon = Draft();
        sermon.SetSpeakers([Guid.NewGuid()], Now);
        sermon.SetVideo(new VideoLink(VideoProvider.YouTube, "y0jPz7KFw_o"), Now);

        Assert.Throws<DomainRuleException>(() => sermon.Schedule(Now.AddMinutes(-1), Now));
    }

    [Fact]
    public void A_published_sermon_keeps_at_least_one_kind_of_media()
    {
        var sermon = Draft();
        sermon.SetSpeakers([Guid.NewGuid()], Now);
        sermon.AttachAudio(ReadyAudio(), Now);
        sermon.Publish(Now);

        Assert.Throws<DomainRuleException>(() => sermon.RemoveAudio(Now));
    }

    [Fact]
    public void Archived_sermons_are_frozen_until_restored()
    {
        var sermon = Draft();
        sermon.Archive(Now);

        Assert.Throws<DomainRuleException>(() => sermon.SetScripture([], Now));
        sermon.Restore(Now);
        sermon.SetScripture([], Now);
        Assert.Equal(SermonStatus.Draft, sermon.Status);
    }

    [Fact]
    public void Pending_uploads_cannot_be_attached()
    {
        var pending = MediaAsset.StartUpload(MediaKind.Audio, "sunday.mp3", "audio/mpeg", 100, null, Now);

        Assert.Throws<DomainRuleException>(() => Draft().AttachAudio(pending, Now));
    }

    [Fact]
    public void Search_text_covers_speakers_series_scripture_and_topics()
    {
        var sermon = Draft();
        Assert.True(ScriptureReference.TryParse("Psalm 42:1-11", out var psalm));
        sermon.SetScripture([psalm], Now);
        sermon.UpdateDetails(sermon.Title, sermon.PreachedOn, null, null, null, ["Longing"], Now);

        sermon.RefreshSearchText(["Israel Phiri"], "Psalms of lament");

        Assert.Contains("Israel Phiri", sermon.SearchText, StringComparison.Ordinal);
        Assert.Contains("Psalms of lament", sermon.SearchText, StringComparison.Ordinal);
        Assert.Contains("Psalm 42:1", sermon.SearchText, StringComparison.Ordinal);
        Assert.Contains("Longing", sermon.SearchText, StringComparison.Ordinal);
    }
}

public sealed class VideoLinkTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=y0jPz7KFw_o")]
    [InlineData("https://youtube.com/watch?feature=share&v=y0jPz7KFw_o")]
    [InlineData("https://youtu.be/y0jPz7KFw_o?t=42")]
    [InlineData("https://www.youtube.com/embed/y0jPz7KFw_o")]
    [InlineData("https://www.youtube.com/live/y0jPz7KFw_o")]
    [InlineData("y0jPz7KFw_o")]
    public void Accepts_youtube_urls_and_ids(string input)
    {
        Assert.True(VideoLink.TryParseYouTube(input, out var video));
        Assert.Equal("y0jPz7KFw_o", video.ExternalId);
    }

    [Theory]
    [InlineData("https://vimeo.com/12345")]
    [InlineData("not a link")]
    [InlineData("")]
    public void Rejects_anything_else(string input)
    {
        Assert.False(VideoLink.TryParseYouTube(input, out _));
    }
}

public sealed class MediaAssetTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Rejects_wrong_types_and_oversized_files()
    {
        Assert.Throws<DomainRuleException>(() => MediaAsset.StartUpload(MediaKind.Audio, "x.exe", "application/octet-stream", 10, null, Now));
        Assert.Throws<DomainRuleException>(() => MediaAsset.StartUpload(MediaKind.NotesPdf, "notes.pdf", "application/pdf", 21L * 1024 * 1024, null, Now));
    }

    [Fact]
    public void Storage_keys_are_unguessable_and_keep_a_safe_extension()
    {
        var asset = MediaAsset.StartUpload(MediaKind.Audio, "../../Sunday Service.MP3", "audio/mpeg", 10, null, Now);

        Assert.Matches("^audio/2026/[0-9a-f]{32}\\.mp3$", asset.StorageKey);
        Assert.Equal("Sunday Service.MP3", asset.OriginalFileName);
    }

    [Fact]
    public void An_incomplete_upload_is_not_ready()
    {
        var asset = MediaAsset.StartUpload(MediaKind.Audio, "a.mp3", "audio/mpeg", 1000, null, Now);

        Assert.Throws<DomainRuleException>(() => asset.MarkReady(999, 60));
        Assert.Equal(MediaAssetStatus.Pending, asset.Status);
    }

    [Fact]
    public void Listening_to_the_end_marks_the_sermon_finished()
    {
        var position = PlaybackPosition.Start(Guid.NewGuid(), Guid.NewGuid(), Now);

        position.Update(600, 3600, Now);
        Assert.False(position.Completed);

        position.Update(3580, 3600, Now);
        Assert.True(position.Completed);
    }
}
