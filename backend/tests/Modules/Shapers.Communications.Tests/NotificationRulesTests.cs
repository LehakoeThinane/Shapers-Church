using Shapers.Communications.Domain;

namespace Shapers.Communications.Tests;

public sealed class NotificationRulesTests
{
    private static readonly TimeSpan Sast = TimeSpan.FromHours(2);

    [Theory]
    [InlineData(10, 0, false, 10, 0, 0)] // daytime: now
    [InlineData(20, 59, false, 20, 59, 0)] // just before quiet hours: now
    [InlineData(21, 0, false, 7, 0, 1)] // quiet hours start: next morning
    [InlineData(23, 30, false, 7, 0, 1)]
    [InlineData(3, 15, false, 7, 0, 0)] // after midnight: this morning
    [InlineData(23, 30, true, 23, 30, 0)] // urgent ("we're live") is never held back
    public void Quiet_hours_hold_non_urgent_messages_until_seven(int hour, int minute, bool urgent, int expectedHour, int expectedMinute, int dayOffset)
    {
        var now = new DateTimeOffset(2026, 10, 4, hour, minute, 0, Sast);

        var at = QuietHours.DeliverAt(now, urgent);

        var expected = new DateTimeOffset(2026, 10, 4 + dayOffset, expectedHour, expectedMinute, 0, Sast);
        Assert.Equal(expected, at);
    }

    [Theory]
    [InlineData("ExponentPushToken[abc123]", true)]
    [InlineData("ExpoPushToken[abc123]", true)]
    [InlineData("fcm-raw-token", false)]
    [InlineData("ExponentPushToken[abc", false)]
    [InlineData(null, false)]
    public void Only_expo_push_tokens_are_accepted(string? token, bool valid) => Assert.Equal(valid, Device.IsExpoToken(token));

    [Fact]
    public void A_notification_queues_one_delivery_per_channel_and_always_reaches_the_inbox()
    {
        var now = new DateTimeOffset(2026, 10, 4, 10, 0, 0, Sast);
        var withPush = Notification.Create(Guid.NewGuid(), Topic.Sermons, "New sermon", "Faith that works", "/sermon/faith", "sermon:1", false, [Channel.Push, Channel.Push], now);
        var inboxOnly = Notification.Create(Guid.NewGuid(), Topic.Sermons, "New sermon", "Faith that works", "/sermon/faith", "sermon:1", false, [], now);

        Assert.Single(withPush.Deliveries);
        Assert.Equal(DeliveryStatus.Pending, withPush.Deliveries[0].Status);
        Assert.Empty(inboxOnly.Deliveries);
    }

    [Fact]
    public void Links_must_stay_inside_the_app() =>
        Assert.Throws<DomainRuleException>(() =>
            Notification.Create(Guid.NewGuid(), Topic.Sermons, "t", "b", "https://evil.example", "k", false, [], DateTimeOffset.UtcNow));

    [Fact]
    public void Failed_deliveries_retry_with_a_pause_then_give_up()
    {
        var now = new DateTimeOffset(2026, 10, 4, 10, 0, 0, Sast);
        var delivery = Notification.Create(Guid.NewGuid(), Topic.Events, "t", "b", null, "k", false, [Channel.Push], now).Deliveries[0];

        delivery.Failed("timeout", now);
        Assert.Equal(DeliveryStatus.Pending, delivery.Status);
        Assert.Equal(now.AddMinutes(5), delivery.NotBefore);

        delivery.Failed("timeout", now);
        delivery.Failed("timeout", now);
        Assert.Equal(DeliveryStatus.Failed, delivery.Status);
        Assert.Equal(3, delivery.Attempts);
    }

    [Fact]
    public void Push_is_on_by_default_and_email_only_for_events_and_announcements()
    {
        Assert.All(Enum.GetValues<Topic>(), t => Assert.True(TopicPreference.Default(t, Channel.Push)));
        Assert.True(TopicPreference.Default(Topic.Events, Channel.Email));
        Assert.True(TopicPreference.Default(Topic.Announcements, Channel.Email));
        Assert.False(TopicPreference.Default(Topic.Sermons, Channel.Email));
        Assert.False(TopicPreference.Default(Topic.Prayer, Channel.Email));
    }

    [Fact]
    public void Long_text_is_shortened_to_fit_a_lock_screen()
    {
        var n = Notification.Create(Guid.NewGuid(), Topic.Sermons, new string('t', 200), new string('b', 800), null, "k", false, [], DateTimeOffset.UtcNow);
        Assert.Equal(Notification.MaxTitle, n.Title.Length);
        Assert.Equal(Notification.MaxBody, n.Body.Length);
        Assert.EndsWith("…", n.Body, StringComparison.Ordinal);
    }
}
