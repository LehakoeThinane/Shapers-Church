using Microsoft.EntityFrameworkCore;
using Shapers.People.Contracts;
using Shapers.Platform.Auditing;
using Shapers.Platform.Authorization;
using Shapers.Platform.Messaging;
using Shapers.Prayer.Contracts;
using Shapers.Prayer.Domain;

namespace Shapers.Prayer.Application;

/// <summary>Members: ask for prayer, see the wall, pray for others, and manage their own requests.</summary>
public sealed class PrayerService(IPrayerDb db, IPeopleDirectory people, ICurrentUser currentUser, TimeProvider clock)
{
    /// <summary>A generous limit that still stops a flood.</summary>
    public const int MaxRequestsPerDay = 5;

    private static readonly Error SignIn = Error.Unauthorized("prayer.sign_in", "Sign in to use the prayer wall.");
    private static readonly Error NotFound = Error.NotFound("prayer.not_found", "Prayer request not found.");

    public async Task<Result<MyPrayerRequestDto>> SubmitAsync(SubmitPrayerRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.PersonId is not { } me)
        {
            return SignIn;
        }

        if (!request.Consent)
        {
            return new Error("prayer.consent_required", "Please confirm that the church may keep your request so we can pray for you.");
        }

        var now = clock.GetUtcNow();
        var since = now.AddDays(-1);
        if (await db.Requests.CountAsync(r => r.PersonId == me && r.CreatedAt > since, cancellationToken) >= MaxRequestsPerDay)
        {
            return new Error("prayer.too_many", "You've sent several requests today. Our pastors will be in touch.", ErrorKind.RateLimited);
        }

        var person = await people.GetAsync(me, cancellationToken);
        if (person is null)
        {
            return SignIn;
        }

        var prayer = PrayerRequest.Submit(
            me,
            ScopePath.Parse(person.Scope),
            request.Text,
            request.ShareOnWall ? PrayerVisibility.Wall : PrayerVisibility.PastorsOnly,
            request.Anonymous,
            PrayerSource.App,
            now);
        db.Requests.Add(prayer);
        await db.SaveChangesAsync(cancellationToken);
        return ToMine(prayer, 0);
    }

    public async Task<Result<IReadOnlyList<PrayerWallItemDto>>> WallAsync(int page, CancellationToken cancellationToken)
    {
        if (currentUser.PersonId is not { } me)
        {
            return SignIn;
        }

        const int pageSize = 30;
        var rows = await db.Requests.AsNoTracking()
            .Where(r => r.Status == PrayerStatus.OnWall)
            .OrderByDescending(r => r.ReviewedAt)
            .Skip(Math.Max(0, page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new
            {
                Request = r,
                Count = db.Responses.Count(x => x.RequestId == r.Id),
                Mine = db.Responses.Any(x => x.RequestId == r.Id && x.PersonId == me),
            })
            .ToListAsync(cancellationToken);
        var names = await people.GetManyAsync(rows.Where(r => !r.Request.Anonymous).Select(r => r.Request.PersonId).Distinct().ToList(), cancellationToken);

        return rows.Select(r => new PrayerWallItemDto(
                r.Request.Id,
                r.Request.WallName(names.GetValueOrDefault(r.Request.PersonId)?.DisplayName ?? "A friend"),
                r.Request.WallText!,
                r.Request.ReviewedAt ?? r.Request.CreatedAt,
                r.Count,
                r.Mine,
                r.Request.PersonId == me,
                r.Request.AnsweredAt is not null))
            .ToList();
    }

    /// <summary>"I prayed". Saying it twice counts once.</summary>
    public async Task<Result<int>> PrayedAsync(Guid requestId, CancellationToken cancellationToken)
    {
        if (currentUser.PersonId is not { } me)
        {
            return SignIn;
        }

        if (!await db.Requests.AnyAsync(r => r.Id == requestId && r.Status == PrayerStatus.OnWall, cancellationToken))
        {
            return NotFound;
        }

        if (!await db.Responses.AnyAsync(x => x.RequestId == requestId && x.PersonId == me, cancellationToken))
        {
            db.Responses.Add(PrayerResponse.Record(requestId, me, clock.GetUtcNow()));
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // A double tap raced the first one; the unique key kept a single response.
            }
        }

        return await db.Responses.CountAsync(x => x.RequestId == requestId, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<MyPrayerRequestDto>>> MineAsync(CancellationToken cancellationToken)
    {
        if (currentUser.PersonId is not { } me)
        {
            return SignIn;
        }

        var rows = await db.Requests.AsNoTracking()
            .Where(r => r.PersonId == me)
            .OrderByDescending(r => r.CreatedAt)
            .Take(100)
            .Select(r => new { Request = r, Count = db.Responses.Count(x => x.RequestId == r.Id) })
            .ToListAsync(cancellationToken);
        return rows.Select(r => ToMine(r.Request, r.Count)).ToList();
    }

    public Task<Result<MyPrayerRequestDto>> MarkAnsweredAsync(Guid requestId, AnswerRequest request, CancellationToken cancellationToken) =>
        EditMineAsync(requestId, (r, now) => r.MarkAnswered(request.Note, now), cancellationToken);

    public Task<Result<MyPrayerRequestDto>> WithdrawAsync(Guid requestId, CancellationToken cancellationToken) =>
        EditMineAsync(requestId, (r, now) => r.Withdraw(now), cancellationToken);

    private async Task<Result<MyPrayerRequestDto>> EditMineAsync(Guid requestId, Action<PrayerRequest, DateTimeOffset> change, CancellationToken cancellationToken)
    {
        if (currentUser.PersonId is not { } me)
        {
            return SignIn;
        }

        var prayer = await db.Requests.SingleOrDefaultAsync(r => r.Id == requestId && r.PersonId == me, cancellationToken);
        if (prayer is null)
        {
            return NotFound;
        }

        change(prayer, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return ToMine(prayer, await db.Responses.CountAsync(x => x.RequestId == requestId, cancellationToken));
    }

    private static MyPrayerRequestDto ToMine(PrayerRequest r, int prayedCount) => new(
        r.Id, r.Text, r.WallText, r.Visibility, r.Anonymous, r.Status, r.ReviewNote, prayedCount, r.CreatedAt, r.WallUntil, r.AnsweredAt, r.AnswerNote);
}

/// <summary>Pastors and reviewers. Every list of requests is a sensitive read and is audited.</summary>
public sealed class PrayerAdminService(IPrayerDb db, IPeopleDirectory people, IAuthorizer authorizer, ICurrentUser currentUser, IAuditLog audit, TimeProvider clock)
{
    private static readonly Error NotFound = Error.NotFound("prayer.not_found", "Prayer request not found.");

    /// <summary>Requests waiting for the wall, oldest first, for reviewers.</summary>
    public async Task<IReadOnlyList<PrayerAdminDto>> AwaitingReviewAsync(CancellationToken cancellationToken)
    {
        var scopes = await authorizer.ScopesForAsync(PrayerPermissions.RequestsModerate, cancellationToken);
        var rows = await ListAsync(db.Requests.AsNoTracking().WithinScopes(r => r.Scope, scopes).Where(r => r.Status == PrayerStatus.AwaitingReview).OrderBy(r => r.CreatedAt), cancellationToken);
        await audit.RecordAsync(new AuditRecord("prayer.requests.review_queue_viewed", "prayer_request", null, Details: new { count = rows.Count }, IsSensitiveRead: true), cancellationToken);
        return rows;
    }

    /// <summary>Everything the pastor may see, newest first. Closed requests stay visible until retention removes them.</summary>
    public async Task<IReadOnlyList<PrayerAdminDto>> AllAsync(PrayerStatus? status, CancellationToken cancellationToken)
    {
        var scopes = await authorizer.ScopesForAsync(PrayerPermissions.RequestsView, cancellationToken);
        var query = db.Requests.AsNoTracking().WithinScopes(r => r.Scope, scopes);
        if (status is { } s)
        {
            query = query.Where(r => r.Status == s);
        }

        var rows = await ListAsync(query.OrderByDescending(r => r.CreatedAt).Take(200), cancellationToken);
        await audit.RecordAsync(new AuditRecord("prayer.requests.viewed", "prayer_request", null, Details: new { status = status?.ToString(), count = rows.Count }, IsSensitiveRead: true), cancellationToken);
        return rows;
    }

    public Task<Result<PrayerAdminDto>> ApproveAsync(Guid id, ReviewRequest request, CancellationToken cancellationToken) =>
        ReviewAsync(id, "prayer.request.approved", (r, by, now) => r.Approve(request.WallText, by, now), cancellationToken);

    public Task<Result<PrayerAdminDto>> KeepWithPastorsAsync(Guid id, ReviewRequest request, CancellationToken cancellationToken) =>
        ReviewAsync(id, "prayer.request.kept_with_pastors", (r, by, now) => r.KeepWithPastors(request.Note, by, now), cancellationToken);

    public Task<Result<PrayerAdminDto>> TakeDownAsync(Guid id, CancellationToken cancellationToken) =>
        ReviewAsync(id, "prayer.request.taken_down", (r, by, now) => r.TakeDown(by, now), cancellationToken);

    private async Task<Result<PrayerAdminDto>> ReviewAsync(Guid id, string action, Action<PrayerRequest, Guid, DateTimeOffset> change, CancellationToken cancellationToken)
    {
        var prayer = await db.Requests.SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (prayer is null || currentUser.UserId is not { } by
            || !await authorizer.CanAsync(PrayerPermissions.RequestsModerate, ScopePath.Parse(prayer.Scope), cancellationToken))
        {
            return NotFound;
        }

        change(prayer, by, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord(action, "prayer_request", prayer.Id.ToString(), ScopePath.Parse(prayer.Scope)), cancellationToken);
        return (await ToDtosAsync([prayer], cancellationToken))[0];
    }

    private async Task<IReadOnlyList<PrayerAdminDto>> ListAsync(IQueryable<PrayerRequest> query, CancellationToken cancellationToken) =>
        await ToDtosAsync(await query.ToListAsync(cancellationToken), cancellationToken);

    private async Task<IReadOnlyList<PrayerAdminDto>> ToDtosAsync(IReadOnlyList<PrayerRequest> requests, CancellationToken cancellationToken)
    {
        var ids = requests.Select(r => r.Id).ToList();
        var counts = await db.Responses.AsNoTracking().Where(x => ids.Contains(x.RequestId)).GroupBy(x => x.RequestId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var names = await people.GetManyAsync(requests.Select(r => r.PersonId).Distinct().ToList(), cancellationToken);
        return requests.Select(r => new PrayerAdminDto(
                r.Id, r.PersonId, names.GetValueOrDefault(r.PersonId)?.DisplayName ?? "Unknown", r.Text, r.WallText, r.Visibility, r.Anonymous, r.Status, r.Source,
                r.Scope, counts.GetValueOrDefault(r.Id), r.CreatedAt, r.ReviewedAt, r.ReviewNote, r.AnsweredAt, r.AnswerNote))
            .ToList();
    }
}

/// <summary>Someone ticked "please pray for me" on a connect card: file it with the pastors.</summary>
public sealed class FileConnectCardPrayer(IPrayerDb db, TimeProvider clock) : IIntegrationEventHandler<ConnectCardSubmittedIntegrationEvent>
{
    public async Task HandleAsync(ConnectCardSubmittedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        if (!integrationEvent.Reasons.Contains("Prayer", StringComparer.Ordinal))
        {
            return;
        }

        var text = string.IsNullOrWhiteSpace(integrationEvent.PrayerText) ? "Asked for prayer on a connect card." : integrationEvent.PrayerText;
        if (text.Length > PrayerRequest.MaxLength)
        {
            text = text[..PrayerRequest.MaxLength];
        }

        db.Requests.Add(PrayerRequest.Submit(
            integrationEvent.PersonId, ScopePath.Parse(integrationEvent.Scope), text, PrayerVisibility.PastorsOnly, anonymous: false, PrayerSource.ConnectCard, clock.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Nightly: requests leave the wall 30 days after they were approved.</summary>
public sealed class WallExpiryJob(IPrayerDb db, TimeProvider clock)
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var due = await db.Requests.Where(r => r.Status == PrayerStatus.OnWall && r.WallUntil <= now).ToListAsync(cancellationToken);
        var expired = due.Count(r => r.ExpireFromWall(now));
        await db.SaveChangesAsync(cancellationToken);
        return expired;
    }
}

/// <summary>A person's prayer requests and "I prayed" marks, for export and erasure.</summary>
public sealed class PrayerPersonalData(IPrayerDb db) : Shapers.Platform.Privacy.IPersonalDataSource
{
    public string Name => "Prayer requests";

    public async Task<object?> ExportAsync(Guid personId, CancellationToken cancellationToken)
    {
        var requests = await db.Requests.AsNoTracking().Where(r => r.PersonId == personId).OrderBy(r => r.CreatedAt)
            .Select(r => new { r.CreatedAt, r.Text, r.WallText, Visibility = r.Visibility.ToString(), r.Anonymous, Status = r.Status.ToString(), r.AnsweredAt, r.AnswerNote })
            .ToListAsync(cancellationToken);
        var prayedFor = await db.Responses.AsNoTracking().CountAsync(x => x.PersonId == personId, cancellationToken);
        return requests.Count == 0 && prayedFor == 0 ? null : new { Requests = requests, TimesYouPrayedForOthers = prayedFor };
    }

    public async Task<int> EraseAsync(Guid personId, CancellationToken cancellationToken) =>
        await db.Responses.Where(x => x.PersonId == personId).ExecuteDeleteAsync(cancellationToken)
        + await db.Requests.Where(r => r.PersonId == personId).ExecuteDeleteAsync(cancellationToken);
}

/// <summary>Nightly: prayer requests are deleted a year after they were made.</summary>
public sealed class PrayerRetentionJob(IPrayerDb db, TimeProvider clock)
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(365);

    public Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var cutoff = clock.GetUtcNow() - Retention;
        return db.Requests.Where(r => r.CreatedAt < cutoff).ExecuteDeleteAsync(cancellationToken);
    }
}
