using Shapers.Communications.Domain;

namespace Shapers.Communications.Tests;

public sealed class AnnouncementRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Author = Guid.NewGuid();
    private static readonly Guid Approver = Guid.NewGuid();

    private static Announcement For(string scope, DateTimeOffset? sendAt = null) =>
        Announcement.Draft("Family fun day", "Join us on Saturday!", "/event/family-fun-day", ScopePath.Parse(scope), true, sendAt, Author, Now);

    [Theory]
    [InlineData("shapers", true)]
    [InlineData("shapers.campus_rivonia", true)]
    [InlineData("shapers.campus_rivonia.ministry_youth", false)]
    [InlineData("shapers.ministry_worship", false)]
    public void Only_whole_church_and_campus_announcements_need_a_second_approver(string scope, bool needsApproval)
    {
        var a = For(scope);
        a.Submit(Now);

        Assert.Equal(needsApproval, a.NeedsApproval);
        Assert.Equal(needsApproval ? AnnouncementStatus.AwaitingApproval : AnnouncementStatus.Queued, a.Status);
    }

    [Fact]
    public void The_author_cannot_approve_their_own_announcement()
    {
        var a = For("shapers");
        a.Submit(Now);

        Assert.Throws<DomainRuleException>(() => a.Approve(Author, Now));
        a.Approve(Approver, Now);
        Assert.Equal(AnnouncementStatus.Queued, a.Status);
    }

    [Fact]
    public void Returning_to_draft_clears_the_approval_and_keeps_the_note()
    {
        var a = For("shapers");
        a.Submit(Now);
        a.Approve(Approver, Now);

        a.ReturnToDraft("Please add the start time.", Now);

        Assert.Equal(AnnouncementStatus.Draft, a.Status);
        Assert.Null(a.ApprovedByUserId);
        Assert.Equal("Please add the start time.", a.ReturnNote);
    }

    [Fact]
    public void Only_drafts_can_be_edited()
    {
        var a = For("shapers.ministry_worship");
        a.Submit(Now);
        Assert.Throws<DomainRuleException>(() => a.Edit("New title", "Body", null, ScopePath.Parse("shapers"), false, null, Now));
    }

    [Fact]
    public void A_scheduled_announcement_is_due_only_from_its_send_time()
    {
        var a = For("shapers.ministry_worship", Now.AddHours(2));
        a.Submit(Now);

        Assert.False(a.IsDue(Now));
        Assert.True(a.IsDue(Now.AddHours(2)));
    }

    [Fact]
    public void A_send_time_in_the_past_is_refused_on_submit()
    {
        var a = For("shapers.ministry_worship", Now.AddHours(-1));
        Assert.Throws<DomainRuleException>(() => a.Submit(Now));
    }

    [Fact]
    public void Links_must_point_inside_the_app() =>
        Assert.Throws<DomainRuleException>(() =>
            Announcement.Draft("Title", "Body", "https://example.com", ScopePath.Parse("shapers"), false, null, Author, Now));

    [Fact]
    public void Sent_announcements_cannot_be_cancelled()
    {
        var a = For("shapers.ministry_worship");
        a.Submit(Now);
        a.MarkSent(10, Now);
        Assert.Throws<DomainRuleException>(() => a.Cancel(Now));
    }
}
