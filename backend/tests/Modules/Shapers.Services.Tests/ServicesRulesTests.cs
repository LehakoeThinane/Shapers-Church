using Shapers.Services.Domain;
using Shapers.SharedKernel;

namespace Shapers.Services.Tests;

public sealed class ServicesRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 9, 0, 0, TimeSpan.Zero);
    private static readonly ScopePath Rivonia = ScopePath.Parse("shapers.rivonia");
    private static readonly DateOnly Sunday = new(2026, 10, 11);

    private static PlanItem Item(string title, int minutes, PlanItemKind kind = PlanItemKind.Item, Guid? songId = null) =>
        new(Guid.Empty, kind, title, minutes * 60, null, songId, null, null, null);

    [Fact]
    public void Start_times_follow_the_lengths_and_headers_take_no_time()
    {
        var plan = Plan.Create("Sunday 09:00", Sunday, new TimeOnly(9, 0), Rivonia, Now);
        plan.SetItems([Item("Worship", 10, PlanItemKind.Header), Item("Welcome", 5), Item("Way Maker", 6, PlanItemKind.Song, Guid.NewGuid()), Item("Sermon", 40)], Now);

        var times = plan.StartTimes();

        Assert.Equal(new TimeOnly(9, 0), times[plan.Items[1].Id]);
        Assert.Equal(new TimeOnly(9, 5), times[plan.Items[2].Id]);
        Assert.Equal(new TimeOnly(9, 11), times[plan.Items[3].Id]);
        Assert.Equal(0, plan.Items[0].LengthSeconds);
        Assert.Equal(51 * 60, plan.TotalSeconds);
    }

    [Fact]
    public void A_song_item_needs_a_song() =>
        Assert.Throws<DomainRuleException>(() => Plan.Create("Sunday", Sunday, new TimeOnly(9, 0), Rivonia, Now).SetItems([Item("Untitled song", 5, PlanItemKind.Song)], Now));

    [Fact]
    public void A_plan_from_a_service_type_copies_its_order_with_new_items()
    {
        var position = Guid.NewGuid();
        var type = ServiceType.Create("Sunday 09:00", Rivonia, new TimeOnly(9, 0), [Item("Welcome", 5), Item("Sermon", 40)], [new PositionNeed(position, 2), new PositionNeed(position, 1)]);

        var plan = Plan.FromType(type, Sunday, null, Now);

        Assert.Equal("Sunday 09:00", plan.Title);
        Assert.Equal(["Welcome", "Sermon"], plan.Items.Select(i => i.Title));
        Assert.DoesNotContain(plan.Items, i => type.Items.Any(t => t.Id == i.Id));
        Assert.Equal(3, Assert.Single(plan.Needs).Count);
    }

    [Fact]
    public void The_run_sheet_skips_headers_and_ends_after_the_last_item()
    {
        var plan = Plan.Create("Sunday", Sunday, new TimeOnly(9, 0), Rivonia, Now);
        plan.SetItems([Item("Worship", 0, PlanItemKind.Header), Item("Welcome", 5), Item("Sermon", 40)], Now);

        plan.GoLive(null, Now);
        Assert.Equal("Welcome", plan.Items.Single(i => i.Id == plan.LiveItemId).Title);

        plan.Step(1, Now.AddMinutes(5));
        Assert.Equal("Sermon", plan.Items.Single(i => i.Id == plan.LiveItemId).Title);
        Assert.Equal(Now.AddMinutes(5), plan.LiveItemStartedAt);

        plan.Step(-1, Now.AddMinutes(6));
        Assert.Equal("Welcome", plan.Items.Single(i => i.Id == plan.LiveItemId).Title);

        plan.Step(1, Now);
        plan.Step(1, Now);
        Assert.False(plan.IsLive);
    }

    [Fact]
    public void Asking_someone_to_serve_raises_one_request_and_a_no_tells_the_schedulers()
    {
        var plan = Plan.Create("Sunday", Sunday, new TimeOnly(9, 0), Rivonia, Now);
        var team = Team.Create("Worship", Rivonia, null, false, Now);
        var keys = TeamPosition.Create(team.Id, "Keys", 1);

        var assignment = Assignment.Request(plan, team, keys, Guid.NewGuid(), Guid.NewGuid(), Now);
        Assert.IsType<ServingRequested>(Assert.Single(assignment.DomainEvents));
        assignment.ClearDomainEvents();

        assignment.Decline("Away", plan.Title, team.Name, keys.Name, Now);
        assignment.Decline("Away", plan.Title, team.Name, keys.Name, Now);
        Assert.Equal(AssignmentStatus.Declined, assignment.Status);
        Assert.Single(assignment.DomainEvents.OfType<ServingDeclined>());

        assignment.Accept(Now);
        Assert.Null(assignment.DeclineReason);
    }

    [Fact]
    public void Only_people_who_said_yes_are_reminded_and_only_once()
    {
        var plan = Plan.Create("Sunday", Sunday, new TimeOnly(9, 0), Rivonia, Now);
        var team = Team.Create("Production", Rivonia, null, false, Now);
        var sound = TeamPosition.Create(team.Id, "Sound", 1);
        var assignment = Assignment.Request(plan, team, sound, Guid.NewGuid(), Guid.NewGuid(), Now);

        Assert.False(assignment.Remind(plan.Title, team.Name, sound.Name, Now));
        assignment.Accept(Now);
        Assert.True(assignment.Remind(plan.Title, team.Name, sound.Name, Now));
        Assert.False(assignment.Remind(plan.Title, team.Name, sound.Name, Now));
    }

    [Theory]
    [InlineData(10, 9)]
    [InlineData(1, 2)]
    public void Away_dates_must_be_in_order_and_not_in_the_past(int fromDay, int toDay)
    {
        var today = new DateOnly(2026, 10, 6);
        Assert.Throws<DomainRuleException>(() => Blockout.Create(Guid.NewGuid(), new DateOnly(2026, 10, fromDay), new DateOnly(2026, 10, toDay), null, today));
    }

    [Fact]
    public void A_song_always_has_an_arrangement_and_a_numeric_ccli_number()
    {
        var song = Song.Create("Way Maker", ScopePath.Parse("shapers"), Now);
        Assert.Equal("Default", Assert.Single(song.Arrangements).Name);

        Assert.Throws<DomainRuleException>(() => song.Update("Way Maker", "Sinach", "CCLI-123", [], null, null, [], Now));
        song.Update("Way Maker", "Sinach", "7115744", ["Faith", "faith", "Worship"], null, "https://www.youtube.com/watch?v=abc", [new Arrangement(Guid.Empty, "Default", "E", 68, null, null, null, null)], Now);
        Assert.Equal(["Faith", "Worship"], song.Themes);
        Assert.NotEqual(Guid.Empty, song.Arrangements[0].Id);
    }
}
