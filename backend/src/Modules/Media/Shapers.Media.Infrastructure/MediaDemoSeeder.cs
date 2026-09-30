using Microsoft.EntityFrameworkCore;
using Shapers.Church.Contracts;
using Shapers.Media.Domain;

namespace Shapers.Media.Infrastructure;

/// <summary>
/// Development only: a little real-looking content so the app isn't empty on first run. Uses the church's
/// public material (a sermon already on its website and the 2026 conference theme). Never runs when any
/// sermon exists, and never outside Development.
/// </summary>
internal sealed class MediaDemoSeeder(MediaDbContext db, IChurchDirectory church, TimeProvider clock)
{
    private static readonly TimeZoneInfo Johannesburg = TimeZoneInfo.FindSystemTimeZoneById("Africa/Johannesburg");

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        if (await db.Sermons.AnyAsync(cancellationToken) || await db.Speakers.AnyAsync(cancellationToken))
        {
            return;
        }

        var now = clock.GetUtcNow();
        var root = ScopePath.Parse((await church.GetRootScopeAsync(cancellationToken)).Path);
        var video = new VideoLink(VideoProvider.YouTube, "y0jPz7KFw_o");

        var pastor = Speaker.Create("Israel Phiri", "Senior Pastor", null, null);
        var series = Series.Create("The servant songs of Isaiah", root, now);
        series.Update(series.Title, "The 2026 conference theme: four songs about the servant of the Lord.", new DateOnly(2026, 9, 3), null);
        db.Speakers.Add(pastor);
        db.Series.Add(series);

        var psalm = Sermon.Create("Deep calls unto deep", new DateOnly(2026, 8, 30), root, now);
        psalm.UpdateDetails(psalm.Title, psalm.PreachedOn, null, "When the soul is downcast, God's deep answers our deep.",
            "# Deep calls unto deep\n- Thirst for God (v1-2)\n- Remember and hope (v4-5)\n\n> Why are you in despair, my soul? Hope in God!",
            ["Hope", "Lament"], now);
        psalm.SetSpeakers([pastor.Id], now);
        psalm.SetScripture([Parse("Psalm 42:1-11")], now);
        psalm.SetVideo(video, now);
        psalm.RefreshSearchText([pastor.Name], null);
        psalm.Publish(now);

        var servant = Sermon.Create("Behold my servant", new DateOnly(2026, 9, 6), root, now);
        servant.UpdateDetails(servant.Title, servant.PreachedOn, series.Id, "The first servant song: gentle, faithful, bringing justice to the nations.",
            "# Behold my servant\n1. Chosen and delighted in\n2. Gentle with the bruised reed\n3. Faithful until justice is established\n\n> A bruised reed he will not break.",
            ["Servanthood"], now);
        servant.SetSpeakers([pastor.Id], now);
        servant.SetScripture([Parse("Isaiah 42:1-9"), Parse("Matthew 12:18-21")], now);
        servant.SetVideo(video, now);
        servant.RefreshSearchText([pastor.Name], series.Title);
        servant.Publish(now);

        db.Sermons.AddRange(psalm, servant);

        // Next Sunday 09:00 in Johannesburg, so the Live tab and Home show "Starts ...".
        var local = TimeZoneInfo.ConvertTime(now, Johannesburg);
        var daysToSunday = ((int)DayOfWeek.Sunday - (int)local.DayOfWeek + 7) % 7;
        var sunday = local.Date.AddDays(daysToSunday == 0 && local.Hour >= 9 ? 7 : daysToSunday).AddHours(9);
        var start = new DateTimeOffset(sunday, Johannesburg.GetUtcOffset(sunday));
        var stream = Livestream.Schedule("Sunday service", root, start, now);
        stream.Update(stream.Title, start, video, "# This Sunday\nWelcome to Shapers Church.", "https://pay.yoco.com/shapers-church", now);
        stream.AddCue(Parse("Isaiah 42:1-4"), "Behold, my servant, whom I uphold; my chosen, in whom my soul delights.", now);
        db.Livestreams.Add(stream);

        await db.SaveChangesAsync(cancellationToken);
    }

    private static ScriptureReference Parse(string text) =>
        ScriptureReference.TryParse(text, out var reference) ? reference : throw new InvalidOperationException(text);
}
