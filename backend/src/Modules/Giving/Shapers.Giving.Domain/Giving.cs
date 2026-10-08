namespace Shapers.Giving.Domain;

/// <summary>What a gift is for: tithe, offering, building fund. The church sets its own.</summary>
public sealed class Fund : AggregateRoot<Guid>
{
    private Fund()
    {
    }

    public string Name { get; private set; } = null!;

    public string? Description { get; private set; }

    public int Order { get; private set; }

    public bool IsArchived { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Fund Create(string name, string? description, int order, DateTimeOffset now)
    {
        var fund = new Fund { Id = Guid.CreateVersion7(), CreatedAt = now };
        fund.Update(name, description, order);
        return fund;
    }

    public void Update(string name, string? description, int order)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 60)
        {
            throw new DomainRuleException("giving.fund_name", "Give the fund a name of up to 60 characters.");
        }

        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim()[..Math.Min(description.Trim().Length, 200)];
        Order = order;
    }

    public void Archive() => IsArchived = true;

    public void Restore() => IsArchived = false;
}

public enum GiftMethod
{
    /// <summary>Paid by card online, through the payment provider.</summary>
    Card,

    /// <summary>A bank transfer, recorded by the finance team.</summary>
    Eft,

    Cash,
}

public enum GiftStatus
{
    /// <summary>Waiting for the payment provider to confirm the card payment.</summary>
    Pending,

    Received,

    /// <summary>The card payment failed or was cancelled. Kept briefly, then removed by retention.</summary>
    Failed,
}

/// <summary>
/// A gift to a fund. Who gives and how much is personal information (and church giving can reveal religious belief),
/// so only staff with the giving permission see it, and every read is audited. Gifts are kept for five years for SARS;
/// erasing a giver removes their name from the record but keeps the amount.
/// </summary>
public sealed class Gift : AggregateRoot<Guid>
{
    /// <summary>R5: below this, card fees outweigh the gift.</summary>
    public const long MinimumCents = 500;

    /// <summary>R500,000: a typo guard. Larger gifts are recorded by the finance team.</summary>
    public const long MaximumCents = 50_000_000;

    private Gift()
    {
    }

    public Guid FundId { get; private set; }

    /// <summary>The fund's name at the time, so old records still read right after a fund is renamed.</summary>
    public string FundName { get; private set; } = null!;

    public long AmountCents { get; private set; }

    public string Currency { get; private set; } = "ZAR";

    public GiftMethod Method { get; private set; }

    public GiftStatus Status { get; private set; }

    /// <summary>The giver's church record, when they're signed in or the finance team chose them.</summary>
    public Guid? PersonId { get; private set; }

    /// <summary>A giver without a record (on the website, or a name on a bank statement).</summary>
    public string? GiverName { get; private set; }

    /// <summary>Where a website giver's receipt goes. Never shown in lists.</summary>
    public string? GiverEmail { get; private set; }

    public string? Note { get; private set; }

    /// <summary>The church calendar date of the gift.</summary>
    public DateOnly GivenOn { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ReceivedAt { get; private set; }

    /// <summary>The payment provider's checkout, for matching its confirmation.</summary>
    public string? ProviderCheckoutId { get; private set; }

    public string? ProviderPaymentId { get; private set; }

    /// <summary>The finance team member who recorded an EFT or cash gift.</summary>
    public Guid? RecordedByUserId { get; private set; }

    public static Gift StartCard(Fund fund, long amountCents, Guid? personId, string? giverName, string? giverEmail, DateOnly today, DateTimeOffset now)
    {
        if (personId is null && (string.IsNullOrWhiteSpace(giverName) || string.IsNullOrWhiteSpace(giverEmail)))
        {
            throw new DomainRuleException("giving.giver_required", "Tell us your name and email so we can send your receipt.");
        }

        return New(fund, amountCents, GiftMethod.Card, GiftStatus.Pending, personId, giverName, giverEmail, null, today, now, null);
    }

    /// <summary>An EFT or cash gift the finance team has seen arrive.</summary>
    public static Gift Record(Fund fund, long amountCents, GiftMethod method, Guid? personId, string? giverName, string? note, DateOnly givenOn, Guid recordedBy, DateTimeOffset now)
    {
        if (method == GiftMethod.Card)
        {
            throw new DomainRuleException("giving.card_is_online", "Card gifts come in through online giving; record EFT and cash here.");
        }

        if (givenOn > DateOnly.FromDateTime(now.UtcDateTime).AddDays(1))
        {
            throw new DomainRuleException("giving.date_in_future", "The gift date can't be in the future.");
        }

        var gift = New(fund, amountCents, method, GiftStatus.Received, personId, giverName, null, note, givenOn, now, recordedBy);
        gift.ReceivedAt = now;
        return gift;
    }

    public void AttachCheckout(string checkoutId) => ProviderCheckoutId = checkoutId;

    /// <summary>The provider confirmed the payment. Confirming twice changes nothing.</summary>
    public bool MarkReceived(string? paymentId, long amountCents, DateTimeOffset now)
    {
        if (Status == GiftStatus.Received)
        {
            return false;
        }

        if (amountCents != AmountCents)
        {
            throw new DomainRuleException("giving.amount_mismatch", "The payment amount doesn't match the gift.");
        }

        Status = GiftStatus.Received;
        ProviderPaymentId = paymentId;
        ReceivedAt = now;
        return true;
    }

    public bool MarkFailed()
    {
        if (Status != GiftStatus.Pending)
        {
            return false;
        }

        Status = GiftStatus.Failed;
        return true;
    }

    /// <summary>Erasure: the giver is forgotten, the amount stays for the church's financial records.</summary>
    public void ForgetGiver()
    {
        PersonId = null;
        GiverName = null;
        GiverEmail = null;
        Note = null;
    }

    public static long ToCents(decimal rand)
    {
        if (decimal.Round(rand, 2) != rand)
        {
            throw new DomainRuleException("giving.amount_cents", "Use rands and cents, e.g. 250 or 250.50.");
        }

        return (long)(rand * 100);
    }

    private static Gift New(
        Fund fund, long amountCents, GiftMethod method, GiftStatus status, Guid? personId, string? giverName, string? giverEmail, string? note, DateOnly givenOn, DateTimeOffset now, Guid? recordedBy)
    {
        if (fund.IsArchived)
        {
            throw new DomainRuleException("giving.fund_closed", $"{fund.Name} isn't taking gifts any more.");
        }

        if (amountCents < MinimumCents || amountCents > MaximumCents)
        {
            throw new DomainRuleException("giving.amount_range", "Give between R5 and R500,000. For larger gifts, please speak to the church office.");
        }

        return new Gift
        {
            Id = Guid.CreateVersion7(),
            FundId = fund.Id,
            FundName = fund.Name,
            AmountCents = amountCents,
            Method = method,
            Status = status,
            PersonId = personId,
            GiverName = Clean(giverName, 120),
            GiverEmail = Clean(giverEmail, 254)?.ToLowerInvariant(),
            Note = Clean(note, 300),
            GivenOn = givenOn,
            CreatedAt = now,
            RecordedByUserId = recordedBy,
        };
    }

    private static string? Clean(string? text, int max) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim()[..Math.Min(text.Trim().Length, max)];
}
