using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Shapers.Platform.Authorization;
using Shapers.Platform.Persistence;
using Shapers.SharedKernel;

namespace Shapers.Platform.Auditing;

/// <summary>
/// One row in the append-only audit log. A database trigger rejects UPDATE and DELETE on this table,
/// so entries are evidence for POPIA access and change requests.
/// </summary>
public sealed class AuditEntry
{
    public Guid Id { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    public Guid? ActorUserId { get; init; }

    public Guid? ActorPersonId { get; init; }

    /// <summary>What happened, as <c>module.entity.verb</c>, e.g. <c>people.person.viewed</c>.</summary>
    public string Action { get; init; } = null!;

    public string EntityType { get; init; } = null!;

    public string? EntityId { get; init; }

    public string? Scope { get; init; }

    /// <summary>True for reads of special personal information (religious belief, health, children).</summary>
    public bool IsSensitiveRead { get; init; }

    public string? IpAddress { get; init; }

    public string? CorrelationId { get; init; }

    public string? Details { get; init; }
}

public sealed record AuditRecord(
    string Action,
    string EntityType,
    string? EntityId,
    ScopePath? Scope = null,
    object? Details = null,
    bool IsSensitiveRead = false);

public interface IAuditLog
{
    Task RecordAsync(AuditRecord record, CancellationToken cancellationToken);
}

internal sealed class AuditLog(
    PlatformDbContext db,
    ICurrentUser currentUser,
    IHttpContextAccessor httpContextAccessor,
    TimeProvider clock) : IAuditLog
{
    /// <summary>Five full years (1827 days covers any two leap days), matching the database rule in the AllowAuditRetention migration.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(1827);

    public async Task RecordAsync(AuditRecord record, CancellationToken cancellationToken)
    {
        var http = httpContextAccessor.HttpContext;
        db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.CreateVersion7(),
            OccurredAt = clock.GetUtcNow(),
            ActorUserId = currentUser.UserId,
            ActorPersonId = currentUser.PersonId,
            Action = record.Action,
            EntityType = record.EntityType,
            EntityId = record.EntityId,
            Scope = record.Scope?.Value,
            IsSensitiveRead = record.IsSensitiveRead,
            IpAddress = http?.Connection.RemoteIpAddress?.ToString(),
            CorrelationId = http?.TraceIdentifier,
            Details = record.Details is null ? null : JsonSerializer.Serialize(record.Details, JsonSerializerOptions.Web),
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
