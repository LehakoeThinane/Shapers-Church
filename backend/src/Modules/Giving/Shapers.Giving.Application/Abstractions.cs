using Microsoft.EntityFrameworkCore;
using Shapers.Giving.Contracts;
using Shapers.Giving.Domain;
using Shapers.Platform.Authorization;

namespace Shapers.Giving.Application;

public interface IGivingDb
{
    DbSet<Fund> Funds { get; }

    DbSet<Gift> Gifts { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public sealed class GivingPermissionProvider : IPermissionProvider
{
    public IEnumerable<PermissionDefinition> GetPermissions() =>
    [
        new(GivingPermissions.View, "giving", "See gifts, totals and givers' statements (reads are audited)", IsSensitive: true),
        new(GivingPermissions.Manage, "giving", "Record EFT and cash gifts, and set up the funds", IsSensitive: true),
    ];
}

/// <summary>Settings for giving. Bank details are public (they're on the website); provider keys live in Key Vault.</summary>
public sealed class GivingOptions
{
    public const string SectionName = "Giving";

    /// <summary>"None" (card giving uses <see cref="CardLinkUrl"/>) or "Yoco".</summary>
    public string Provider { get; set; } = "None";

    /// <summary>The church's existing payment link, used while online giving isn't set up.</summary>
    public string? CardLinkUrl { get; set; }

    /// <summary>The website's giving page; the payment page returns here with the result.</summary>
    public string ReturnUrl { get; set; } = "https://www.shaperschurch.com/give";

    public EftDetails Eft { get; set; } = new();
}

public sealed class EftDetails
{
    public string? Bank { get; set; }

    public string? AccountName { get; set; }

    public string? AccountNumber { get; set; }

    public string? BranchCode { get; set; }

    public string? Reference { get; set; }
}

/// <summary>The payment provider's checkout page for one gift.</summary>
public sealed record CheckoutRequest(Guid GiftId, long AmountCents, string Currency, string SuccessUrl, string CancelUrl, string FailureUrl);

public sealed record CheckoutStarted(string CheckoutId, string RedirectUrl);

/// <summary>What the provider told us about a payment, after its signature was checked.</summary>
public sealed record PaymentNotice(string CheckoutId, string? PaymentId, long AmountCents, bool Succeeded);

/// <summary>
/// A card payment provider. Cards are only ever entered on the provider's own page; the platform never sees them.
/// </summary>
public interface IPaymentProvider
{
    bool IsConfigured { get; }

    Task<CheckoutStarted> StartCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken);

    /// <summary>Reads a notification from the provider. Null when the signature doesn't check out or it isn't about a payment.</summary>
    PaymentNotice? ReadNotice(IReadOnlyDictionary<string, string> headers, string body);
}

/// <summary>When no provider is set up: card giving falls back to the church's payment link.</summary>
public sealed class NoPaymentProvider : IPaymentProvider
{
    public bool IsConfigured => false;

    public Task<CheckoutStarted> StartCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("No payment provider is configured.");

    public PaymentNotice? ReadNotice(IReadOnlyDictionary<string, string> headers, string body) => null;
}

public sealed record FundDto(Guid Id, string Name, string? Description, int Order, bool IsArchived);

public sealed record EftDto(string? Bank, string? AccountName, string? AccountNumber, string? BranchCode, string? Reference);

/// <summary>What the app and website need to show the giving page.</summary>
public sealed record GivingPageDto(IReadOnlyList<FundDto> Funds, bool CardGivingEnabled, string? CardLinkUrl, EftDto Eft);

/// <summary>Start a card gift. Signed-in members are the giver; others give a name and email for the receipt.</summary>
public sealed record StartGiftRequest(Guid FundId, decimal Amount, string? Name, string? Email);

public sealed record StartGiftResponse(Guid GiftId, string RedirectUrl);

public sealed record MyGiftDto(Guid Id, string FundName, long AmountCents, GiftMethod Method, DateOnly GivenOn);

public sealed record GiftDto(Guid Id, string FundName, long AmountCents, GiftMethod Method, GiftStatus Status, Guid? PersonId, string GiverName, string? Note, DateOnly GivenOn, DateTimeOffset CreatedAt);

public sealed record TotalDto(string Label, long AmountCents, int Count);

public sealed record GivingOverviewDto(string Month, long TotalCents, int Count, IReadOnlyList<TotalDto> ByFund, IReadOnlyList<TotalDto> ByMethod, IReadOnlyList<GiftDto> Latest);

public sealed record GiftPageDto(IReadOnlyList<GiftDto> Items, int Total, long TotalCents, int Page, int PageSize);

/// <summary>An EFT or cash gift. Give <see cref="PersonId"/> for someone with a church record, or <see cref="GiverName"/> otherwise.</summary>
public sealed record RecordGiftRequest(Guid FundId, decimal Amount, GiftMethod Method, Guid? PersonId, string? GiverName, string? Note, DateOnly GivenOn);

public sealed record SaveFundRequest(string Name, string? Description, int Order);

/// <summary>A giver's year: every gift received, and the totals, for their records and a Section 18A certificate.</summary>
public sealed record GivingStatementDto(Guid PersonId, string PersonName, int Year, IReadOnlyList<MyGiftDto> Gifts, long TotalCents, IReadOnlyList<TotalDto> ByFund);
