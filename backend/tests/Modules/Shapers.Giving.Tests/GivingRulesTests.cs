using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shapers.Giving.Domain;
using Shapers.Giving.Infrastructure;

namespace Shapers.Giving.Tests;

public sealed class GivingRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 11);

    [Theory]
    [InlineData("250", 25000)]
    [InlineData("250.50", 25050)]
    [InlineData("0.05", 5)]
    public void Amounts_become_cents(string rand, long cents) =>
        Assert.Equal(cents, Gift.ToCents(decimal.Parse(rand, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void Fractions_of_a_cent_are_refused() =>
        Assert.Throws<DomainRuleException>(() => Gift.ToCents(10.005m));

    [Theory]
    [InlineData(499)]
    [InlineData(50_000_001)]
    public void Gifts_must_be_within_the_limits(long cents) =>
        Assert.Throws<DomainRuleException>(() => Gift.StartCard(Fund.Create("Tithe", null, 0, Now), cents, Guid.NewGuid(), null, null, Today, Now));

    [Fact]
    public void A_visitor_must_give_a_name_and_email_and_an_archived_fund_takes_no_gifts()
    {
        var fund = Fund.Create("Tithe", null, 0, Now);
        Assert.Throws<DomainRuleException>(() => Gift.StartCard(fund, 10_000, null, "Thandi", null, Today, Now));

        fund.Archive();
        Assert.Equal("giving.fund_closed", Assert.Throws<DomainRuleException>(() => Gift.StartCard(fund, 10_000, Guid.NewGuid(), null, null, Today, Now)).Code);
    }

    [Fact]
    public void A_card_gift_is_received_once_and_only_for_the_right_amount()
    {
        var gift = Gift.StartCard(Fund.Create("Offering", null, 1, Now), 25_000, null, "Thandi Mokoena", "Thandi@Example.org", Today, Now);
        Assert.Equal(GiftStatus.Pending, gift.Status);
        Assert.Equal("thandi@example.org", gift.GiverEmail);

        Assert.Throws<DomainRuleException>(() => gift.MarkReceived("p_1", 2_500, Now));
        Assert.True(gift.MarkReceived("p_1", 25_000, Now));
        Assert.False(gift.MarkReceived("p_1", 25_000, Now));
        Assert.False(gift.MarkFailed());
        Assert.Equal(GiftStatus.Received, gift.Status);

        gift.ForgetGiver();
        Assert.Null(gift.GiverName);
        Assert.Null(gift.GiverEmail);
        Assert.Equal(25_000, gift.AmountCents);
    }

    [Fact]
    public void Only_eft_and_cash_are_recorded_by_hand_and_not_in_the_future()
    {
        var fund = Fund.Create("Tithe", null, 0, Now);
        Assert.Throws<DomainRuleException>(() => Gift.Record(fund, 10_000, GiftMethod.Card, null, "Anonymous", null, Today, Guid.NewGuid(), Now));
        Assert.Throws<DomainRuleException>(() => Gift.Record(fund, 10_000, GiftMethod.Eft, null, "Anonymous", null, Today.AddDays(5), Guid.NewGuid(), Now));
        Assert.Equal(GiftStatus.Received, Gift.Record(fund, 10_000, GiftMethod.Eft, null, "Anonymous", null, Today, Guid.NewGuid(), Now).Status);
    }

    [Fact]
    public void Only_a_correctly_signed_recent_webhook_is_believed()
    {
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        var clock = new FakeTimeProvider(Now);
        var yoco = new YocoPaymentProvider(new HttpClient(), Options.Create(new YocoOptions { SecretKey = "sk_test", WebhookSecret = "whsec_" + secret }), clock);
        const string body = """{"type":"payment.succeeded","payload":{"id":"p_123","amount":25000,"metadata":{"checkoutId":"ch_abc"}}}""";
        var timestamp = Now.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var signature = "v1," + Convert.ToBase64String(HMACSHA256.HashData(Convert.FromBase64String(secret), Encoding.UTF8.GetBytes($"msg_1.{timestamp}.{body}")));
        Dictionary<string, string> Headers(string sig, string ts) => new(StringComparer.OrdinalIgnoreCase) { ["webhook-id"] = "msg_1", ["webhook-timestamp"] = ts, ["webhook-signature"] = sig };

        var notice = yoco.ReadNotice(Headers(signature, timestamp), body);
        Assert.NotNull(notice);
        Assert.Equal("ch_abc", notice.CheckoutId);
        Assert.Equal(25_000, notice.AmountCents);
        Assert.True(notice.Succeeded);

        Assert.Null(yoco.ReadNotice(Headers(signature, timestamp), body.Replace("25000", "250000", StringComparison.Ordinal)));
        Assert.Null(yoco.ReadNotice(Headers("v1,AAAA", timestamp), body));
        clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Null(yoco.ReadNotice(Headers(signature, timestamp), body));
    }
}
