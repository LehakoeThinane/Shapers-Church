using Shapers.Prayer.Domain;

namespace Shapers.Prayer.Tests;

public sealed class PrayerRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 8, 0, 0, TimeSpan.Zero);
    private static readonly ScopePath Campus = ScopePath.Parse("shapers.campus_rivonia");
    private static readonly Guid Reviewer = Guid.NewGuid();

    private static PrayerRequest Ask(PrayerVisibility visibility = PrayerVisibility.Wall, bool anonymous = false) =>
        PrayerRequest.Submit(Guid.NewGuid(), Campus, "  Please pray for my mother's operation on Friday.  ", visibility, anonymous, PrayerSource.App, Now);

    [Fact]
    public void Wall_requests_wait_for_review_and_pastors_only_requests_never_do()
    {
        Assert.Equal(PrayerStatus.AwaitingReview, Ask().Status);
        Assert.Equal(PrayerStatus.WithPastors, Ask(PrayerVisibility.PastorsOnly).Status);
        Assert.Equal("Please pray for my mother's operation on Friday.", Ask().Text);
    }

    [Fact]
    public void A_reviewer_can_reword_what_the_wall_shows_while_pastors_keep_the_original()
    {
        var request = Ask();

        request.Approve("Please pray for my family this week.", Reviewer, Now);

        Assert.Equal(PrayerStatus.OnWall, request.Status);
        Assert.Equal("Please pray for my family this week.", request.WallText);
        Assert.Equal("Please pray for my mother's operation on Friday.", request.Text);
        Assert.Equal(Now + PrayerRequest.WallDuration, request.WallUntil);
    }

    [Fact]
    public void Approving_without_new_wording_shows_the_original()
    {
        var request = Ask();
        request.Approve("  ", Reviewer, Now);
        Assert.Equal(request.Text, request.WallText);
    }

    [Fact]
    public void Pastors_only_requests_cannot_be_put_on_the_wall()
    {
        var request = Ask(PrayerVisibility.PastorsOnly);
        Assert.Throws<DomainRuleException>(() => request.Approve(null, Reviewer, Now));
    }

    [Fact]
    public void Keeping_a_request_with_the_pastors_tells_the_requester_why()
    {
        var request = Ask();
        request.KeepWithPastors("It names someone else, so we've kept it with the pastors.", Reviewer, Now);

        Assert.Equal(PrayerStatus.WithPastors, request.Status);
        Assert.Null(request.WallText);
        Assert.Equal("It names someone else, so we've kept it with the pastors.", request.ReviewNote);
    }

    [Fact]
    public void The_wall_shows_first_names_only_or_nothing_when_anonymous()
    {
        Assert.Equal("Thandi", Ask().WallName("Thandi Mokoena"));
        Assert.Equal("Someone from Shapers", Ask(anonymous: true).WallName("Thandi Mokoena"));
    }

    [Fact]
    public void Requests_leave_the_wall_after_thirty_days()
    {
        var request = Ask();
        request.Approve(null, Reviewer, Now);

        Assert.False(request.ExpireFromWall(Now.AddDays(29)));
        Assert.True(request.ExpireFromWall(Now.AddDays(30)));
        Assert.Equal(PrayerStatus.Closed, request.Status);
        Assert.False(request.ExpireFromWall(Now.AddDays(31)));
    }

    [Fact]
    public void Withdrawing_and_taking_down_close_the_request()
    {
        var withdrawn = Ask();
        withdrawn.Approve(null, Reviewer, Now);
        withdrawn.Withdraw(Now.AddHours(1));
        Assert.Equal(PrayerStatus.Closed, withdrawn.Status);

        var takenDown = Ask();
        takenDown.Approve(null, Reviewer, Now);
        takenDown.TakeDown(Reviewer, Now.AddHours(1));
        Assert.Equal(PrayerStatus.Closed, takenDown.Status);
        Assert.Throws<DomainRuleException>(() => takenDown.TakeDown(Reviewer, Now.AddHours(2)));
    }

    [Fact]
    public void Marking_answered_keeps_the_first_date_and_the_latest_note()
    {
        var request = Ask();
        request.MarkAnswered(null, Now);
        request.MarkAnswered("The operation went well!", Now.AddDays(2));

        Assert.Equal(Now, request.AnsweredAt);
        Assert.Equal("The operation went well!", request.AnswerNote);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_requests_are_refused(string text) =>
        Assert.Throws<DomainRuleException>(() => PrayerRequest.Submit(Guid.NewGuid(), Campus, text, PrayerVisibility.Wall, false, PrayerSource.App, Now));

    [Fact]
    public void Overlong_requests_are_refused() =>
        Assert.Throws<DomainRuleException>(() =>
            PrayerRequest.Submit(Guid.NewGuid(), Campus, new string('a', PrayerRequest.MaxLength + 1), PrayerVisibility.Wall, false, PrayerSource.App, Now));
}
