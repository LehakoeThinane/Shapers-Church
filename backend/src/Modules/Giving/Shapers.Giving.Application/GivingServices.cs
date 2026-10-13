using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shapers.Church.Contracts;
using Shapers.Giving.Contracts;
using Shapers.Giving.Domain;
using Shapers.People.Contracts;
using Shapers.Platform.Auditing;
using Shapers.Platform.Authorization;
using Shapers.Platform.Calendar;
using Shapers.Platform.Text;

namespace Shapers.Giving.Application;

/// <summary>Members and visitors: the giving page, starting a card gift, the provider's confirmation, and "my giving".</summary>
public sealed class GivingService(IGivingDb db, IPaymentProvider provider, ICurrentUser currentUser, IOptions<GivingOptions> options, TimeProvider clock)
{
    public async Task<GivingPageDto> PageAsync(CancellationToken cancellationToken)
    {
        var funds = await db.Funds.AsNoTracking().Where(f => !f.IsArchived).OrderBy(f => f.Order).ThenBy(f => f.Name)
            .Select(f => new FundDto(f.Id, f.Name, f.Description, f.Order, f.IsArchived))
            .ToListAsync(cancellationToken);
        var o = options.Value;
        return new GivingPageDto(funds, provider.IsConfigured, o.CardLinkUrl, new EftDto(o.Eft.Bank, o.Eft.AccountName, o.Eft.AccountNumber, o.Eft.BranchCode, o.Eft.Reference));
    }

    public async Task<Result<StartGiftResponse>> StartAsync(StartGiftRequest request, CancellationToken cancellationToken)
    {
        if (!provider.IsConfigured)
        {
            return Error.Conflict("giving.card_unavailable", "Online card giving isn't set up yet. Please use the church's payment link or a bank transfer.");
        }

        var fund = await db.Funds.SingleOrDefaultAsync(f => f.Id == request.FundId, cancellationToken);
        if (fund is null)
        {
            return Error.NotFound("giving.fund_not_found", "Choose what your gift is for.");
        }

        string? email = null;
        if (currentUser.PersonId is null && !ContactNormaliser.TryNormaliseEmail(request.Email ?? "", out email))
        {
            return new Error("giving.email_invalid", "Check your email address: your receipt goes there.");
        }

        var now = clock.GetUtcNow();
        var gift = Gift.StartCard(fund, Gift.ToCents(request.Amount), currentUser.PersonId, currentUser.PersonId is null ? request.Name : null, email, ChurchTime.DateOf(now), now);
        db.Gifts.Add(gift);
        await db.SaveChangesAsync(cancellationToken);

        var back = options.Value.ReturnUrl.TrimEnd('/');
        var checkout = await provider.StartCheckoutAsync(
            new CheckoutRequest(gift.Id, gift.AmountCents, gift.Currency, $"{back}/thanks?gift={gift.Id}", $"{back}?cancelled=1", $"{back}?failed=1"),
            cancellationToken);
        gift.AttachCheckout(checkout.CheckoutId);
        await db.SaveChangesAsync(cancellationToken);
        return new StartGiftResponse(gift.Id, checkout.RedirectUrl);
    }

    /// <summary>
    /// The provider's word that a payment went through (or didn't). Only signed notices count, and handling the same
    /// notice twice changes nothing. False when the notice isn't genuine.
    /// </summary>
    public async Task<bool> HandleNoticeAsync(IReadOnlyDictionary<string, string> headers, string body, CancellationToken cancellationToken)
    {
        if (provider.ReadNotice(headers, body) is not { } notice)
        {
            return false;
        }

        var gift = await db.Gifts.SingleOrDefaultAsync(g => g.ProviderCheckoutId == notice.CheckoutId, cancellationToken);
        if (gift is null)
        {
            // Not one of ours (e.g. a payment made through the old payment link): nothing to do.
            return true;
        }

        var changed = notice.Succeeded ? gift.MarkReceived(notice.PaymentId, notice.AmountCents, clock.GetUtcNow()) : gift.MarkFailed();
        if (changed)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    public async Task<Result<IReadOnlyList<MyGiftDto>>> MineAsync(CancellationToken cancellationToken)
    {
        if (currentUser.PersonId is not { } me)
        {
            return Error.Unauthorized("giving.sign_in", "Sign in to see your giving.");
        }

        return await db.Gifts.AsNoTracking()
            .Where(g => g.PersonId == me && g.Status == GiftStatus.Received)
            .OrderByDescending(g => g.GivenOn).ThenByDescending(g => g.CreatedAt)
            .Take(200)
            .Select(g => new MyGiftDto(g.Id, g.FundName, g.AmountCents, g.Method, g.GivenOn))
            .ToListAsync(cancellationToken);
    }
}

/// <summary>The finance team: totals, the gifts list, recording EFT and cash, statements and funds. Giving is church-wide.</summary>
public sealed class GivingAdminService(
    IGivingDb db,
    IPeopleDirectory people,
    IChurchDirectory church,
    IAuthorizer authorizer,
    ICurrentUser currentUser,
    IAuditLog audit,
    TimeProvider clock)
{
    private const int PageSize = 50;

    public async Task<Result<GivingOverviewDto>> OverviewAsync(string? month, CancellationToken cancellationToken)
    {
        if (await RootAsync(GivingPermissions.View, cancellationToken) is not { } root)
        {
            return Forbidden;
        }

        var today = ChurchTime.DateOf(clock.GetUtcNow());
        var first = DateOnly.TryParseExact(month ?? "", "yyyy-MM", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var m)
            ? m
            : new DateOnly(today.Year, today.Month, 1);
        var next = first.AddMonths(1);
        var gifts = await Received().Where(g => g.GivenOn >= first && g.GivenOn < next).ToListAsync(cancellationToken);
        var latest = await Received().OrderByDescending(g => g.GivenOn).ThenByDescending(g => g.CreatedAt).Take(10).ToListAsync(cancellationToken);

        await audit.RecordAsync(new AuditRecord("giving.overview.viewed", "giving", null, root, new { month = first.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture) }, IsSensitiveRead: true), cancellationToken);
        return new GivingOverviewDto(
            first.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture),
            gifts.Sum(g => g.AmountCents),
            gifts.Count,
            [.. gifts.GroupBy(g => g.FundName).Select(t => new TotalDto(t.Key, t.Sum(g => g.AmountCents), t.Count())).OrderByDescending(t => t.AmountCents)],
            [.. gifts.GroupBy(g => g.Method).Select(t => new TotalDto(MethodLabel(t.Key), t.Sum(g => g.AmountCents), t.Count())).OrderByDescending(t => t.AmountCents)],
            await ToDtosAsync(latest, cancellationToken));
    }

    public async Task<Result<GiftPageDto>> ListAsync(DateOnly? from, DateOnly? to, Guid? fundId, GiftMethod? method, string? search, int page, CancellationToken cancellationToken)
    {
        if (await RootAsync(GivingPermissions.View, cancellationToken) is not { } root)
        {
            return Forbidden;
        }

        var query = Received();
        if (from is { } f)
        {
            query = query.Where(g => g.GivenOn >= f);
        }

        if (to is { } t)
        {
            query = query.Where(g => g.GivenOn <= t);
        }

        if (fundId is { } fund)
        {
            query = query.Where(g => g.FundId == fund);
        }

        if (method is { } kind)
        {
            query = query.Where(g => g.Method == kind);
        }

        // Names live in People, so the name search runs over the (date-limited) rows here.
        var rows = await query.OrderByDescending(g => g.GivenOn).ThenByDescending(g => g.CreatedAt).Take(5000).ToListAsync(cancellationToken);
        var dtos = await ToDtosAsync(rows, cancellationToken);
        if (!string.IsNullOrWhiteSpace(search))
        {
            dtos = [.. dtos.Where(d => d.GiverName.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase))];
        }

        var current = Math.Max(1, page);
        await audit.RecordAsync(new AuditRecord("giving.gifts.listed", "giving", null, root, new { count = dtos.Count }, IsSensitiveRead: true), cancellationToken);
        return new GiftPageDto([.. dtos.Skip((current - 1) * PageSize).Take(PageSize)], dtos.Count, dtos.Sum(d => d.AmountCents), current, PageSize);
    }

    public async Task<Result<GiftDto>> RecordAsync(RecordGiftRequest request, CancellationToken cancellationToken)
    {
        if (await RootAsync(GivingPermissions.Manage, cancellationToken) is not { } root || currentUser.UserId is not { } by)
        {
            return Forbidden;
        }

        var fund = await db.Funds.SingleOrDefaultAsync(f => f.Id == request.FundId, cancellationToken);
        if (fund is null)
        {
            return Error.NotFound("giving.fund_not_found", "Fund not found.");
        }

        if (request.PersonId is { } personId && await people.GetAsync(personId, cancellationToken) is null)
        {
            return Error.NotFound("giving.person_not_found", "Person not found.");
        }

        if (request.PersonId is null && string.IsNullOrWhiteSpace(request.GiverName))
        {
            return new Error("giving.giver_required", "Choose the person, or type the name on the bank statement (or \"Anonymous\").");
        }

        var gift = Gift.Record(fund, Gift.ToCents(request.Amount), request.Method, request.PersonId, request.PersonId is null ? request.GiverName : null, request.Note, request.GivenOn, by, clock.GetUtcNow());
        db.Gifts.Add(gift);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord("giving.gift.recorded", "gift", gift.Id.ToString(), root, new { method = gift.Method.ToString() }), cancellationToken);
        return (await ToDtosAsync([gift], cancellationToken))[0];
    }

    public async Task<Result<GivingStatementDto>> StatementAsync(Guid personId, int year, CancellationToken cancellationToken)
    {
        if (await RootAsync(GivingPermissions.View, cancellationToken) is not { } root)
        {
            return Forbidden;
        }

        var person = await people.GetAsync(personId, cancellationToken);
        if (person is null)
        {
            return Error.NotFound("giving.person_not_found", "Person not found.");
        }

        var gifts = await Received()
            .Where(g => g.PersonId == personId && g.GivenOn >= new DateOnly(year, 1, 1) && g.GivenOn <= new DateOnly(year, 12, 31))
            .OrderBy(g => g.GivenOn)
            .ToListAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord("giving.statement.viewed", "person", personId.ToString(), root, new { year }, IsSensitiveRead: true), cancellationToken);
        return new GivingStatementDto(
            personId,
            person.DisplayName,
            year,
            [.. gifts.Select(g => new MyGiftDto(g.Id, g.FundName, g.AmountCents, g.Method, g.GivenOn))],
            gifts.Sum(g => g.AmountCents),
            [.. gifts.GroupBy(g => g.FundName).Select(t => new TotalDto(t.Key, t.Sum(g => g.AmountCents), t.Count()))]);
    }

    public async Task<Result<IReadOnlyList<FundDto>>> FundsAsync(CancellationToken cancellationToken) =>
        await RootAsync(GivingPermissions.View, cancellationToken) is null && await RootAsync(GivingPermissions.Manage, cancellationToken) is null
            ? Forbidden
            : await db.Funds.AsNoTracking().OrderBy(f => f.IsArchived).ThenBy(f => f.Order).ThenBy(f => f.Name)
                .Select(f => new FundDto(f.Id, f.Name, f.Description, f.Order, f.IsArchived))
                .ToListAsync(cancellationToken);

    public async Task<Result<FundDto>> CreateFundAsync(SaveFundRequest request, CancellationToken cancellationToken)
    {
        if (await RootAsync(GivingPermissions.Manage, cancellationToken) is not { } root)
        {
            return Forbidden;
        }

        var fund = Fund.Create(request.Name, request.Description, request.Order, clock.GetUtcNow());
        db.Funds.Add(fund);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord("giving.fund.created", "fund", fund.Id.ToString(), root), cancellationToken);
        return ToDto(fund);
    }

    public Task<Result<FundDto>> UpdateFundAsync(Guid id, SaveFundRequest request, CancellationToken cancellationToken) =>
        EditFundAsync(id, "giving.fund.updated", f => f.Update(request.Name, request.Description, request.Order), cancellationToken);

    public Task<Result<FundDto>> ArchiveFundAsync(Guid id, CancellationToken cancellationToken) =>
        EditFundAsync(id, "giving.fund.archived", f => f.Archive(), cancellationToken);

    public Task<Result<FundDto>> RestoreFundAsync(Guid id, CancellationToken cancellationToken) =>
        EditFundAsync(id, "giving.fund.restored", f => f.Restore(), cancellationToken);

    /// <summary>On first start: two funds, so giving works before the church sets up its own.</summary>
    public static readonly (string Name, string Description)[] DefaultFunds = [("Tithe", "Your tithe to the church."), ("Offering", "Offerings and seed.")];

    private static readonly Error Forbidden = Error.Forbidden("giving.forbidden", "You don't have access to giving.");

    private IQueryable<Gift> Received() => db.Gifts.AsNoTracking().Where(g => g.Status == GiftStatus.Received);

    private async Task<Result<FundDto>> EditFundAsync(Guid id, string action, Action<Fund> change, CancellationToken cancellationToken)
    {
        if (await RootAsync(GivingPermissions.Manage, cancellationToken) is not { } root)
        {
            return Forbidden;
        }

        var fund = await db.Funds.SingleOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (fund is null)
        {
            return Error.NotFound("giving.fund_not_found", "Fund not found.");
        }

        change(fund);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord(action, "fund", fund.Id.ToString(), root), cancellationToken);
        return ToDto(fund);
    }

    /// <summary>Giving is church-wide: the permission must be held at the church's root.</summary>
    private async Task<ScopePath?> RootAsync(string permission, CancellationToken cancellationToken)
    {
        var root = ScopePath.Parse((await church.GetRootScopeAsync(cancellationToken)).Path);
        return await authorizer.CanAsync(permission, root, cancellationToken) ? root : null;
    }

    private async Task<IReadOnlyList<GiftDto>> ToDtosAsync(IReadOnlyList<Gift> gifts, CancellationToken cancellationToken)
    {
        var names = await people.GetManyAsync([.. gifts.Where(g => g.PersonId is not null).Select(g => g.PersonId!.Value).Distinct()], cancellationToken);
        return
        [
            .. gifts.Select(g => new GiftDto(
                g.Id,
                g.FundName,
                g.AmountCents,
                g.Method,
                g.Status,
                g.PersonId,
                g.PersonId is { } p ? names.GetValueOrDefault(p)?.DisplayName ?? "Unknown" : g.GiverName ?? "Anonymous",
                g.Note,
                g.GivenOn,
                g.CreatedAt)),
        ];
    }

    private static FundDto ToDto(Fund f) => new(f.Id, f.Name, f.Description, f.Order, f.IsArchived);

    private static string MethodLabel(GiftMethod method) => method switch
    {
        GiftMethod.Card => "Card",
        GiftMethod.Eft => "EFT",
        _ => "Cash",
    };
}

/// <summary>Creates the default funds once.</summary>
public sealed class GivingSeeder(IGivingDb db, TimeProvider clock)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        if (await db.Funds.AnyAsync(cancellationToken))
        {
            return;
        }

        var now = clock.GetUtcNow();
        var order = 0;
        db.Funds.AddRange(GivingAdminService.DefaultFunds.Select(d => Fund.Create(d.Name, d.Description, order++, now)));
        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// A person's gifts for export; for erasure, their name comes off the gifts but the amounts stay, because the church
/// must keep its financial records for SARS.
/// </summary>
public sealed class GivingPersonalData(IGivingDb db) : Shapers.Platform.Privacy.IPersonalDataSource
{
    public string Name => "Giving";

    public async Task<object?> ExportAsync(Guid personId, CancellationToken cancellationToken)
    {
        var gifts = await db.Gifts.AsNoTracking().Where(g => g.PersonId == personId).OrderBy(g => g.GivenOn)
            .Select(g => new { g.GivenOn, g.FundName, Amount = g.AmountCents / 100m, Method = g.Method.ToString(), Status = g.Status.ToString(), g.Note })
            .ToListAsync(cancellationToken);
        return gifts.Count == 0 ? null : new { Gifts = gifts };
    }

    public async Task<int> EraseAsync(Guid personId, CancellationToken cancellationToken)
    {
        var gifts = await db.Gifts.Where(g => g.PersonId == personId).ToListAsync(cancellationToken);
        gifts.ForEach(g => g.ForgetGiver());
        await db.SaveChangesAsync(cancellationToken);
        return gifts.Count;
    }
}

/// <summary>
/// Nightly: gifts are deleted five years after they were given (what SARS requires the church to keep), and card gifts
/// that never went through are deleted after 30 days.
/// </summary>
public sealed class GivingRetentionJob(IGivingDb db, TimeProvider clock)
{
    public const int YearsKept = 5;

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var oldest = ChurchTime.DateOf(now).AddYears(-YearsKept);
        var staleBefore = now.AddDays(-30);
        return await db.Gifts.Where(g => g.GivenOn < oldest).ExecuteDeleteAsync(cancellationToken)
            + await db.Gifts.Where(g => g.Status != GiftStatus.Received && g.CreatedAt < staleBefore).ExecuteDeleteAsync(cancellationToken);
    }
}
