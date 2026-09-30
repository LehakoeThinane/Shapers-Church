using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Shapers.People.Contracts;
using Shapers.Platform.Auditing;
using Shapers.Platform.Authorization;
using Shapers.Platform.Privacy;
using Shapers.Privacy.Contracts;
using Shapers.Privacy.Domain;

namespace Shapers.Privacy.Application;

public interface IPrivacyDb
{
    DbSet<DataRequest> Requests { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public sealed class PrivacyPermissionProvider : IPermissionProvider
{
    public IEnumerable<PermissionDefinition> GetPermissions() =>
    [
        new(PrivacyPermissions.RequestsManage, "privacy", "Handle correction and deletion requests (deleting erases the person)", IsSensitive: true),
    ];
}

public sealed record SubmitDataRequest(DataRequestType Type, string? Details);

public sealed record DecideDataRequest(string? Response);

public sealed record MyDataRequestDto(Guid Id, DataRequestType Type, string? Details, DataRequestStatus Status, DateTimeOffset CreatedAt, DateTimeOffset? DecidedAt, string? Response);

public sealed record DataRequestAdminDto(
    Guid Id,
    Guid PersonId,
    string PersonName,
    DataRequestType Type,
    string? Details,
    DataRequestStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset DueAt,
    bool Overdue,
    DateTimeOffset? DecidedAt,
    string? Response);

/// <summary>Runs export and erasure across every module that stores personal data.</summary>
public sealed class PersonalDataService(IEnumerable<IPersonalDataSource> sources, IAuditLog audit)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        // A file the person reads, not HTML: keep names like O'Brien and non-English letters readable.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<byte[]> ExportAsync(Guid personId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var sections = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var source in sources.OrderBy(s => s.Name, StringComparer.Ordinal))
        {
            if (await source.ExportAsync(personId, cancellationToken) is { } data)
            {
                sections[source.Name] = data;
            }
        }

        var document = new
        {
            About = "Everything Shapers Church holds about you in its app and systems, as of the time below. "
                + "Questions or corrections: info@shaperschurch.com.",
            ExportedAt = now,
            Data = sections,
        };
        await audit.RecordAsync(new AuditRecord("privacy.data.exported", "person", personId.ToString(), Details: new { sections = sections.Keys }, IsSensitiveRead: true), cancellationToken);
        return JsonSerializer.SerializeToUtf8Bytes(document, Json);
    }

    public async Task<IReadOnlyDictionary<string, int>> EraseAsync(Guid personId, string reason, CancellationToken cancellationToken)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            counts[source.Name] = await source.EraseAsync(personId, cancellationToken);
        }

        await audit.RecordAsync(new AuditRecord("privacy.person.erased", "person", personId.ToString(), Details: new { reason, counts }), cancellationToken);
        return counts;
    }
}

/// <summary>Members: download their data, and ask for corrections or deletion.</summary>
public sealed class MyPrivacyService(IPrivacyDb db, PersonalDataService data, ICurrentUser currentUser, TimeProvider clock)
{
    private static readonly Error SignIn = Error.Unauthorized("privacy.sign_in", "Sign in first.");

    public async Task<Result<byte[]>> ExportAsync(CancellationToken cancellationToken) =>
        currentUser.PersonId is { } me ? await data.ExportAsync(me, clock.GetUtcNow(), cancellationToken) : SignIn;

    public async Task<Result<IReadOnlyList<MyDataRequestDto>>> MineAsync(CancellationToken cancellationToken)
    {
        if (currentUser.PersonId is not { } me)
        {
            return SignIn;
        }

        return await db.Requests.AsNoTracking().Where(r => r.PersonId == me).OrderByDescending(r => r.CreatedAt)
            .Select(r => new MyDataRequestDto(r.Id, r.Type, r.Details, r.Status, r.CreatedAt, r.DecidedAt, r.Response))
            .ToListAsync(cancellationToken);
    }

    public async Task<Result<MyDataRequestDto>> SubmitAsync(SubmitDataRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.PersonId is not { } me)
        {
            return SignIn;
        }

        if (await db.Requests.AnyAsync(r => r.PersonId == me && r.Type == request.Type && r.Status == DataRequestStatus.Open, cancellationToken))
        {
            return Error.Conflict("privacy.already_open", "You already have a request like this open. We'll be in touch.");
        }

        var r = DataRequest.Submit(me, request.Type, request.Details, clock.GetUtcNow());
        db.Requests.Add(r);
        await db.SaveChangesAsync(cancellationToken);
        return new MyDataRequestDto(r.Id, r.Type, r.Details, r.Status, r.CreatedAt, r.DecidedAt, r.Response);
    }
}

/// <summary>The Information Officer's queue. Completing a deletion erases the person everywhere; it can't be undone.</summary>
public sealed class DataRequestAdminService(
    IPrivacyDb db,
    PersonalDataService data,
    IPeopleDirectory people,
    ICurrentUser currentUser,
    IAuditLog audit,
    TimeProvider clock)
{
    private static readonly Error NotFound = Error.NotFound("privacy.request_not_found", "Request not found.");

    public async Task<IReadOnlyList<DataRequestAdminDto>> ListAsync(DataRequestStatus? status, CancellationToken cancellationToken)
    {
        var query = db.Requests.AsNoTracking();
        if (status is { } s)
        {
            query = query.Where(r => r.Status == s);
        }

        var rows = await query.OrderBy(r => r.Status).ThenBy(r => r.DueAt).Take(200).ToListAsync(cancellationToken);
        var names = await people.GetManyAsync(rows.Select(r => r.PersonId).Distinct().ToList(), cancellationToken);
        var now = clock.GetUtcNow();
        await audit.RecordAsync(new AuditRecord("privacy.requests.viewed", "data_request", null, Details: new { count = rows.Count }, IsSensitiveRead: true), cancellationToken);
        return rows.Select(r => new DataRequestAdminDto(
                r.Id, r.PersonId, names.GetValueOrDefault(r.PersonId)?.DisplayName ?? "Unknown", r.Type, r.Details, r.Status, r.CreatedAt, r.DueAt, r.IsOverdue(now), r.DecidedAt, r.Response))
            .ToList();
    }

    public async Task<Result<DataRequestAdminDto>> CompleteAsync(Guid id, DecideDataRequest request, CancellationToken cancellationToken)
    {
        var r = await db.Requests.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (r is null || currentUser.UserId is not { } me)
        {
            return NotFound;
        }

        if (r.Status != DataRequestStatus.Open)
        {
            return new Error("privacy.not_open", "This request has already been dealt with.");
        }

        var name = (await people.GetAsync(r.PersonId, cancellationToken))?.DisplayName ?? "Unknown";
        if (r.Type == DataRequestType.Deletion)
        {
            await data.EraseAsync(r.PersonId, "request", cancellationToken);
        }

        r.Complete(me, request.Response, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord("privacy.request.completed", "data_request", r.Id.ToString(), Details: new { type = r.Type.ToString() }), cancellationToken);
        return ToDto(r, name);
    }

    public async Task<Result<DataRequestAdminDto>> DeclineAsync(Guid id, DecideDataRequest request, CancellationToken cancellationToken)
    {
        var r = await db.Requests.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (r is null || currentUser.UserId is not { } me)
        {
            return NotFound;
        }

        r.Decline(me, request.Response ?? string.Empty, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord("privacy.request.declined", "data_request", r.Id.ToString(), Details: new { type = r.Type.ToString() }), cancellationToken);
        return ToDto(r, (await people.GetAsync(r.PersonId, cancellationToken))?.DisplayName ?? "Unknown");
    }

    private DataRequestAdminDto ToDto(DataRequest r, string name) =>
        new(r.Id, r.PersonId, name, r.Type, r.Details, r.Status, r.CreatedAt, r.DueAt, r.IsOverdue(clock.GetUtcNow()), r.DecidedAt, r.Response);
}

/// <summary>Nightly: guests who never became more than a name on a connect card are erased after 90 days.</summary>
public sealed class GuestRetentionJob(IPeopleDirectory people, PersonalDataService data, TimeProvider clock)
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(90);

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var stale = await people.UnverifiedGuestsCreatedBeforeAsync(clock.GetUtcNow() - Retention, cancellationToken);
        foreach (var personId in stale)
        {
            await data.EraseAsync(personId, "retention: unverified guest after 90 days", cancellationToken);
        }

        return stale.Count;
    }
}
