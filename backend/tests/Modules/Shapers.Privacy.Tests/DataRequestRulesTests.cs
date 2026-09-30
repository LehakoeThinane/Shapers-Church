using Shapers.Privacy.Domain;

namespace Shapers.Privacy.Tests;

public sealed class DataRequestRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Officer = Guid.NewGuid();

    [Fact]
    public void Requests_are_due_in_thirty_days_and_show_as_overdue_after()
    {
        var r = DataRequest.Submit(Guid.NewGuid(), DataRequestType.Deletion, null, Now);

        Assert.Equal(Now.AddDays(30), r.DueAt);
        Assert.False(r.IsOverdue(Now.AddDays(30)));
        Assert.True(r.IsOverdue(Now.AddDays(31)));
    }

    [Fact]
    public void A_correction_must_say_what_is_wrong() =>
        Assert.Throws<DomainRuleException>(() => DataRequest.Submit(Guid.NewGuid(), DataRequestType.Correction, "  ", Now));

    [Fact]
    public void Completing_a_deletion_announces_the_erasure()
    {
        var r = DataRequest.Submit(Guid.NewGuid(), DataRequestType.Deletion, "Moving churches", Now);
        r.Complete(Officer, "Done.", Now);

        Assert.Equal(DataRequestStatus.Completed, r.Status);
        Assert.Contains(r.DomainEvents, e => e is PersonErasureCompleted);
        Assert.False(r.IsOverdue(Now.AddDays(60)));
    }

    [Fact]
    public void Declining_needs_a_reason_to_give_the_person()
    {
        var r = DataRequest.Submit(Guid.NewGuid(), DataRequestType.Deletion, null, Now);

        Assert.Throws<DomainRuleException>(() => r.Decline(Officer, " ", Now));
        r.Decline(Officer, "We must keep giving records for five years for SARS.", Now);
        Assert.Equal(DataRequestStatus.Declined, r.Status);
    }

    [Fact]
    public void A_request_can_only_be_decided_once()
    {
        var r = DataRequest.Submit(Guid.NewGuid(), DataRequestType.Correction, "My surname is spelt Mokoena.", Now);
        r.Complete(Officer, "Corrected.", Now);

        Assert.Throws<DomainRuleException>(() => r.Complete(Officer, null, Now));
        Assert.Throws<DomainRuleException>(() => r.Decline(Officer, "No", Now));
    }
}
