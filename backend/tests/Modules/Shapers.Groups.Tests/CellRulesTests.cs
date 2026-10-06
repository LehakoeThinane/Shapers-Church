using Shapers.Groups.Domain;

namespace Shapers.Groups.Tests;

public sealed class CellRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 18, 0, 0, TimeSpan.Zero);
    private static readonly ScopePath Campus = ScopePath.Parse("shapers.rivonia");
    private static readonly DateOnly Tuesday = new(2026, 10, 6);

    [Fact]
    public void Leaders_and_co_leaders_lead_the_cell_members_do_not()
    {
        var (cell, leader, coLeader, member) = NewCell();

        Assert.True(cell.IsLedBy(leader));
        Assert.True(cell.IsLedBy(coLeader));
        Assert.False(cell.IsLedBy(member));
        Assert.True(cell.HasMember(member));
    }

    [Fact]
    public void Someone_who_left_no_longer_leads_or_belongs()
    {
        var (cell, leader, _, _) = NewCell();

        cell.RemoveMember(leader, Now);

        Assert.False(cell.IsLedBy(leader));
        Assert.False(cell.HasMember(leader));
    }

    [Fact]
    public void Adding_an_existing_member_changes_their_role_instead_of_adding_them_twice()
    {
        var (cell, _, _, member) = NewCell();

        cell.AddMember(member, CellRole.CoLeader, Now);

        Assert.Single(cell.ActiveMembers, m => m.PersonId == member);
        Assert.True(cell.IsLedBy(member));
    }

    [Fact]
    public void A_closed_cell_takes_no_changes()
    {
        var (cell, _, _, _) = NewCell();
        cell.Close(Now);

        Assert.Throws<DomainRuleException>(() => cell.AddMember(Guid.NewGuid(), CellRole.Member, Now));
    }

    [Fact]
    public void Only_the_cells_members_can_be_marked_present()
    {
        var (cell, leader, _, member) = NewCell();
        var stranger = Guid.NewGuid();

        var error = Assert.Throws<DomainRuleException>(() => Start(cell, leader, Content() with { AttendeeIds = [member, stranger] }));

        Assert.Equal("groups.not_a_member", error.Code);
    }

    [Fact]
    public void A_visitor_who_did_not_agree_keeps_only_a_first_name()
    {
        var (cell, leader, _, _) = NewCell();

        var report = Start(cell, leader, Content() with { Visitors = [new ReportVisitor("Thabo", "Mokoena", "082 000 0000", "thabo@example.org", false)] });

        var visitor = Assert.Single(report.Visitors);
        Assert.Equal("Thabo", visitor.FirstName);
        Assert.Null(visitor.LastName);
        Assert.Null(visitor.Mobile);
        Assert.Null(visitor.Email);
    }

    [Fact]
    public void A_visitor_who_agreed_needs_a_way_to_be_contacted()
    {
        var (cell, leader, _, _) = NewCell();

        var error = Assert.Throws<DomainRuleException>(() => Start(cell, leader, Content() with { Visitors = [new ReportVisitor("Thabo", null, null, null, true)] }));

        Assert.Equal("groups.visitor_contact", error.Code);
    }

    [Fact]
    public void Submitting_passes_on_only_visitors_who_agreed_and_flags_urgent_follow_ups()
    {
        var (cell, leader, _, member) = NewCell();
        var report = Start(cell, leader, Content() with
        {
            AttendeeIds = [member],
            Visitors = [new ReportVisitor("Thabo", null, "082 000 0000", null, true), new ReportVisitor("Lerato", null, null, null, false)],
            FollowUps = [new ReportFollowUp(Guid.Empty, member, "Sipho", "Lost his job this week.", Urgent: true)],
        });

        report.Submit(cell.Name, Now);

        var submitted = Assert.IsType<CellReportSubmitted>(Assert.Single(report.DomainEvents));
        Assert.Equal("Thabo", Assert.Single(submitted.VisitorsToContact).FirstName);
        Assert.True(submitted.HasUrgentFollowUp);
        Assert.Equal(cell.Name, submitted.CellName);
        Assert.Equal(1, report.MembersPresent);
        Assert.Equal(2, report.VisitorCount);
    }

    [Fact]
    public void A_submitted_report_cannot_be_changed()
    {
        var (cell, leader, _, _) = NewCell();
        var report = Start(cell, leader, Content());
        report.Submit(cell.Name, Now);

        Assert.Throws<DomainRuleException>(() => report.Edit(Content() with { Notes = "Changed" }, Members(cell), Now));
        Assert.Throws<DomainRuleException>(() => report.Submit(cell.Name, Now));
    }

    [Fact]
    public void A_report_for_a_meeting_in_the_future_is_refused()
    {
        var (cell, leader, _, _) = NewCell();

        var error = Assert.Throws<DomainRuleException>(() => Start(cell, leader, Content() with { MeetingDate = Tuesday.AddDays(7) }));

        Assert.Equal("groups.report_future", error.Code);
    }

    [Fact]
    public void Retention_removes_the_personal_parts_but_keeps_the_numbers()
    {
        var (cell, leader, _, member) = NewCell();
        var report = Start(cell, leader, Content() with
        {
            AttendeeIds = [member],
            Notes = "Good discussion.",
            PrayerNeeds = "Sipho's job.",
            FollowUps = [new ReportFollowUp(Guid.Empty, member, "Sipho", "Call him.", Urgent: false)],
            Growth = [new ReportGrowth(member, NextStep.Baptism)],
        });

        Assert.True(report.Redact(Now));

        Assert.Null(report.Notes);
        Assert.Null(report.PrayerNeeds);
        Assert.Empty(report.AttendeeIds);
        Assert.Empty(report.FollowUps);
        Assert.Empty(report.Growth);
        Assert.Equal(1, report.MembersPresent);
        Assert.False(report.Redact(Now));
    }

    [Fact]
    public void Erasure_removes_one_person_from_a_report()
    {
        var (cell, leader, coLeader, member) = NewCell();
        var report = Start(cell, leader, Content() with
        {
            AttendeeIds = [member, coLeader],
            FollowUps = [new ReportFollowUp(Guid.Empty, member, "Sipho", "Call him.", Urgent: false)],
            Growth = [new ReportGrowth(member, NextStep.GrowthTrack)],
        });

        Assert.True(report.RemovePerson(member));

        Assert.Equal([coLeader], report.AttendeeIds);
        Assert.Empty(report.FollowUps);
        Assert.Empty(report.Growth);
    }

    [Fact]
    public void A_pastor_resolves_an_urgent_follow_up()
    {
        var (cell, leader, _, member) = NewCell();
        var report = Start(cell, leader, Content() with { FollowUps = [new ReportFollowUp(Guid.Empty, member, "Sipho", "Call him.", Urgent: true)] });
        report.Submit(cell.Name, Now);
        Assert.True(report.HasOpenUrgentFollowUp);

        report.ResolveFollowUp(report.FollowUps[0].Id, Guid.NewGuid(), Now);

        Assert.False(report.HasOpenUrgentFollowUp);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://example.org/notes")]
    [InlineData("not a link")]
    public void Material_links_must_be_web_addresses(string link)
    {
        var error = Assert.Throws<DomainRuleException>(() => CellMaterial.Write(Guid.NewGuid(), Campus, "Week 1", "Notes", link, null, true, Guid.NewGuid(), Now));

        Assert.Equal("groups.material_link", error.Code);
    }

    private static (Cell Cell, Guid Leader, Guid CoLeader, Guid Member) NewCell()
    {
        var cell = Cell.Create("Rivonia North", Guid.NewGuid(), Campus, DayOfWeek.Tuesday, new TimeOnly(19, 0), "Rivonia", "12 Example Road", Now);
        var (leader, coLeader, member) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        cell.AddMember(leader, CellRole.Leader, Now);
        cell.AddMember(coLeader, CellRole.CoLeader, Now);
        cell.AddMember(member, CellRole.Member, Now);
        return (cell, leader, coLeader, member);
    }

    private static List<Guid> Members(Cell cell) => cell.ActiveMembers.Select(m => m.PersonId).ToList();

    private static CellReport Start(Cell cell, Guid leader, ReportContent content) =>
        CellReport.Start(cell.Id, Campus, content, Members(cell), leader, Now);

    private static ReportContent Content() =>
        new(Tuesday, "Faith that works", null, null, null, null, MultiplicationReadiness.NotYet, [], [], [], []);
}
