using Microsoft.EntityFrameworkCore;
using Shapers.Platform.Auditing;
using Shapers.Platform.Messaging;

namespace Shapers.Platform.Persistence;

/// <summary>Cross-cutting tables: the audit log and the inbox. Lives in the <c>platform</c> schema.</summary>
public sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options) : DbContext(options)
{
    public const string Schema = "platform";

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    public DbSet<InboxRecord> InboxRecords => Set<InboxRecord>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) => configurationBuilder.UseUtcTimestamps();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<AuditEntry>(b =>
        {
            b.ToTable("audit_entries");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).ValueGeneratedNever();
            b.Property(e => e.Action).HasMaxLength(100);
            b.Property(e => e.EntityType).HasMaxLength(100);
            b.Property(e => e.EntityId).HasMaxLength(100);
            b.Property(e => e.Scope).HasMaxLength(512);
            b.Property(e => e.IpAddress).HasMaxLength(64);
            b.Property(e => e.CorrelationId).HasMaxLength(100);
            b.Property(e => e.Details).HasColumnType("jsonb");
            b.HasIndex(e => e.OccurredAt);
            b.HasIndex(e => new { e.EntityType, e.EntityId });
            b.HasIndex(e => e.ActorUserId);
        });

        modelBuilder.Entity<InboxRecord>(b =>
        {
            b.ToTable("inbox_records");
            b.HasKey(r => new { r.EventId, r.Handler });
            b.Property(r => r.Handler).HasMaxLength(300);
        });
    }
}

public sealed class InboxRecord
{
    public Guid EventId { get; init; }

    public string Handler { get; init; } = null!;

    public DateTimeOffset ProcessedAt { get; init; }
}

internal sealed class EfInbox(PlatformDbContext db, TimeProvider clock) : IInbox
{
    public Task<bool> HasProcessedAsync(Guid eventId, string handler, CancellationToken cancellationToken) =>
        db.InboxRecords.AnyAsync(r => r.EventId == eventId && r.Handler == handler, cancellationToken);

    public async Task MarkProcessedAsync(Guid eventId, string handler, CancellationToken cancellationToken)
    {
        db.InboxRecords.Add(new InboxRecord { EventId = eventId, Handler = handler, ProcessedAt = clock.GetUtcNow() });
        await db.SaveChangesAsync(cancellationToken);
    }
}
