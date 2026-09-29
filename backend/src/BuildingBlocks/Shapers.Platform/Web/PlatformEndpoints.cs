using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Shapers.Platform.Authorization;
using Shapers.Platform.Persistence;

namespace Shapers.Platform.Web;

public static class PlatformEndpoints
{
    public sealed record AuditEntryDto(
        Guid Id,
        DateTimeOffset OccurredAt,
        Guid? ActorUserId,
        Guid? ActorPersonId,
        string Action,
        string EntityType,
        string? EntityId,
        string? Scope,
        bool IsSensitiveRead,
        string? IpAddress);

    public static IEndpointRouteBuilder MapPlatformEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/api/admin/audit").WithTags("Audit");

        admin.MapGet("/", async (
                PlatformDbContext db,
                string? entityType,
                string? entityId,
                Guid? actorUserId,
                int page = 1,
                int pageSize = 50,
                CancellationToken cancellationToken = default) =>
            {
                var paging = new PageRequest(page, pageSize);
                var query = db.AuditEntries.AsNoTracking();
                if (!string.IsNullOrWhiteSpace(entityType))
                {
                    query = query.Where(e => e.EntityType == entityType);
                }

                if (!string.IsNullOrWhiteSpace(entityId))
                {
                    query = query.Where(e => e.EntityId == entityId);
                }

                if (actorUserId is not null)
                {
                    query = query.Where(e => e.ActorUserId == actorUserId);
                }

                var total = await query.CountAsync(cancellationToken);
                var items = await query
                    .OrderByDescending(e => e.OccurredAt)
                    .Skip(paging.Skip)
                    .Take(paging.SafePageSize)
                    .Select(e => new AuditEntryDto(
                        e.Id, e.OccurredAt, e.ActorUserId, e.ActorPersonId, e.Action, e.EntityType,
                        e.EntityId, e.Scope, e.IsSensitiveRead, e.IpAddress))
                    .ToListAsync(cancellationToken);

                return TypedResults.Ok(new PagedResult<AuditEntryDto>(items, paging.SafePage, paging.SafePageSize, total));
            })
            .WithName("ListAuditEntries")
            .RequirePermission(PlatformPermissions.AuditView);

        return endpoints;
    }
}
