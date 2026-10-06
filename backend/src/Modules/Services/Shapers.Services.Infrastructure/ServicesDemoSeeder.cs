using Microsoft.EntityFrameworkCore;
using Shapers.Church.Contracts;
using Shapers.Services.Domain;

namespace Shapers.Services.Infrastructure;

/// <summary>
/// Development and the demo only: typical church teams in their categories, a few well-known worship songs, a Sunday
/// template and the coming Sundays, so the pastor can see Services working and say what Shapers actually needs.
/// On a database that already has teams it only fills gaps (missing typical teams, positions and categories).
/// Lyrics are left out: the church adds them under its own CCLI licence.
/// </summary>
internal sealed class ServicesDemoSeeder(ServicesDbContext db, IChurchDirectory church, TimeProvider clock)
{
    private static readonly (string Team, string Category, bool OpenToMinors, string[] Positions, string[] Aliases)[] Teams =
    [
        ("Kids ministry", "Ministries", false, ["Kids pastor", "Kids leader", "Kids helper", "Check-in desk"], ["Kids church"]),
        ("Youth ministry", "Ministries", true, ["Youth pastor", "Youth leader", "Youth helper"], []),
        ("Young adults", "Ministries", false, ["Young adults leader", "Small group host"], []),
        ("Men's ministry", "Ministries", false, ["Men's ministry leader", "Committee member"], []),
        ("Women's ministry", "Ministries", false, ["Women's ministry leader", "Committee member"], []),
        ("Worship", "Disciplines", false, ["Worship pastor", "Worship leader", "Vocals", "Keys", "Acoustic guitar", "Electric guitar", "Bass", "Drums"], []),
        ("Production", "Disciplines", true, ["Production lead", "Sound", "Slides", "Livestream", "Camera", "Lighting"], []),
        ("Hospitality", "Disciplines", true, ["Hospitality lead", "Welcome team", "Ushers", "Refreshments", "Parking"], []),
        ("Prayer ministry", "Disciplines", false, ["Prayer coordinator", "Prayer team"], []),
        ("Administration", "Departments", false, ["Church administrator", "Office volunteer"], []),
        ("Finance", "Departments", false, ["Treasurer", "Counting team"], []),
        ("Facilities", "Departments", false, ["Facilities lead", "Maintenance volunteer"], []),
        ("Growth Track", "Spiritual growth", false, ["Growth Track lead", "Facilitator", "Host"], []),
        ("Discipleship and Bible study", "Spiritual growth", false, ["Discipleship lead", "Teacher", "Facilitator"], []),
    ];

    private static readonly (string Title, string Author, string Ccli, string Key, int Bpm)[] Songs =
    [
        ("Way Maker", "Sinach", "7115744", "E", 68),
        ("Goodness of God", "Ed Cash, Jenn Johnson, Jason Ingram, Ben Fielding, Brian Johnson", "7117726", "Ab", 63),
        ("Graves into Gardens", "Elevation Worship, Brandon Lake", "7138219", "B", 70),
        ("Build My Life", "Pat Barrett, Brett Younker, Karl Martin, Kirby Kaple, Matt Redman", "7070345", "G", 68),
        ("What a Beautiful Name", "Ben Fielding, Brooke Ligertwood", "7068424", "D", 68),
        ("10,000 Reasons (Bless the Lord)", "Matt Redman, Jonas Myrin", "6016351", "G", 73),
    ];

    /// <summary>Who a Sunday usually needs, by position name.</summary>
    private static readonly (string Position, int Count)[] SundayNeeds =
    [
        ("Worship leader", 1), ("Vocals", 2), ("Keys", 1), ("Acoustic guitar", 1), ("Bass", 1), ("Drums", 1),
        ("Sound", 1), ("Slides", 1), ("Livestream", 1),
        ("Welcome team", 2), ("Ushers", 2),
        ("Kids leader", 1), ("Kids helper", 2),
        ("Prayer team", 2),
    ];

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var fresh = !await db.Teams.AnyAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var campus = (await church.GetCampusesAsync(cancellationToken)).FirstOrDefault(c => c.IsPrimary);
        var root = ScopePath.Parse((await church.GetRootScopeAsync(cancellationToken)).Path);
        var scope = ScopePath.Parse(campus?.Scope ?? root.Value);

        var categories = await db.Categories.Where(c => !c.IsArchived).ToDictionaryAsync(c => c.Name, cancellationToken);
        var teams = await db.Teams.ToListAsync(cancellationToken);
        var positions = await db.Positions.Where(p => !p.IsArchived).ToListAsync(cancellationToken);
        var byName = new Dictionary<string, TeamPosition>();

        foreach (var (name, categoryName, openToMinors, positionNames, aliases) in Teams)
        {
            var categoryId = categories.TryGetValue(categoryName, out var category) ? category.Id : (Guid?)null;
            var team = teams.FirstOrDefault(t => t.Name == name || aliases.Contains(t.Name));
            if (team is null)
            {
                team = Team.Create(name, scope, null, openToMinors, now, categoryId);
                db.Teams.Add(team);
            }
            else if (team.CategoryId is null && categoryId is not null)
            {
                team.Update(team.Name, team.Description, team.OpenToMinors, categoryId);
            }

            for (var i = 0; i < positionNames.Length; i++)
            {
                var position = positions.FirstOrDefault(p => p.TeamId == team.Id && p.Name == positionNames[i]);
                if (position is null)
                {
                    position = TeamPosition.Create(team.Id, positionNames[i], i + 1);
                    db.Positions.Add(position);
                }

                byName.TryAdd(positionNames[i], position);
            }
        }

        if (fresh)
        {
            SeedSongsAndSundays(root, scope, byName, now);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private void SeedSongsAndSundays(ScopePath root, ScopePath scope, Dictionary<string, TeamPosition> positions, DateTimeOffset now)
    {
        var songs = Songs.Select(s =>
        {
            var song = Song.Create(s.Title, root, now);
            song.Update(s.Title, s.Author, s.Ccli, ["Worship"], null, null, [new Arrangement(Guid.Empty, "Default", s.Key, s.Bpm, null, null, null, null)], now);
            return song;
        }).ToList();
        db.Songs.AddRange(songs);

        PlanItem Item(PlanItemKind kind, string title, int minutes, Song? song = null, string? leader = null) =>
            new(Guid.Empty, kind, title, minutes * 60, null, song?.Id, song?.Arrangements[0].Id, song?.Arrangements[0].Key, leader);
        var type = ServiceType.Create(
            "Sunday 09:00",
            scope,
            new TimeOnly(9, 0),
            [
                Item(PlanItemKind.Header, "Worship", 0),
                Item(PlanItemKind.Song, songs[0].Title, 6, songs[0]),
                Item(PlanItemKind.Song, songs[1].Title, 6, songs[1]),
                Item(PlanItemKind.Song, songs[2].Title, 6, songs[2]),
                Item(PlanItemKind.Header, "Word", 0),
                Item(PlanItemKind.Item, "Welcome and announcements", 5, leader: "Host"),
                Item(PlanItemKind.Item, "Tithes and offering", 5),
                Item(PlanItemKind.Item, "Sermon", 40, leader: "Senior Pastor"),
                Item(PlanItemKind.Header, "Response", 0),
                Item(PlanItemKind.Item, "Ministry and prayer", 10, leader: "Prayer team"),
                Item(PlanItemKind.Item, "Closing and benediction", 3),
            ],
            SundayNeeds.Select(n => new PositionNeed(positions[n.Position].Id, n.Count)));
        db.ServiceTypes.Add(type);

        // The coming four Sundays, so the plans list and the matrix aren't empty.
        var today = DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(2)).DateTime);
        var sunday = today.AddDays(((int)DayOfWeek.Sunday - (int)today.DayOfWeek + 7) % 7);
        for (var week = 0; week < 4; week++)
        {
            db.Plans.Add(Plan.FromType(type, sunday.AddDays(7 * week), null, now));
        }
    }
}
