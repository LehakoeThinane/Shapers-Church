using Shapers.Privacy.Domain;

namespace Shapers.Privacy.Tests;

public sealed class BreachRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Officer = Guid.NewGuid();

    private static Breach Lost() => Breach.Record("Lost attendance sheet", "The kids' attendance sheet was left in the car park.", Now.AddHours(-2), Officer, Now);

    [Fact]
    public void A_breach_is_flagged_when_the_regulator_has_not_been_told_within_72_hours()
    {
        var breach = Lost();

        Assert.False(breach.NotificationOverdue(Now));
        Assert.True(breach.NotificationOverdue(Now.AddHours(71)));
    }

    [Fact]
    public void A_breach_closes_only_once_the_regulator_is_told_and_it_is_contained()
    {
        var breach = Lost();
        Assert.Throws<DomainRuleException>(() => breach.Close(Now));

        breach.Update(breach.Title, breach.Description, null, "Children's names and allergies", 24, true, null, Now, null, Now);
        Assert.Throws<DomainRuleException>(() => breach.Close(Now));

        breach.Update(breach.Title, breach.Description, null, "Children's names and allergies", 24, true, "Sheet recovered; parents told.", Now, Now, Now);
        breach.Close(Now);
        Assert.Equal(BreachStatus.Closed, breach.Status);
        Assert.False(breach.NotificationOverdue(Now.AddDays(10)));
    }

    [Fact]
    public void The_discovery_date_cannot_be_in_the_future() =>
        Assert.Throws<DomainRuleException>(() => Breach.Record("Title", "What happened", Now.AddDays(1), Officer, Now));

    [Fact]
    public void Notification_dates_cannot_precede_discovery()
    {
        var breach = Lost();
        Assert.Throws<DomainRuleException>(() =>
            breach.Update(breach.Title, breach.Description, null, null, null, false, null, Now.AddDays(-10), null, Now));
    }
}
