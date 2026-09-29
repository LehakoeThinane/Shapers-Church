using Microsoft.EntityFrameworkCore;
using Shapers.Platform.Messaging;
using Shapers.SharedKernel;

namespace Shapers.Platform.Persistence;

/// <summary>
/// Base for a module's DbContext. Each module owns one Postgres schema and never joins across schemas.
/// On save, domain events raised by tracked aggregates are translated into integration events
/// and written to the module's outbox in the same transaction.
/// </summary>
public abstract class ModuleDbContext(DbContextOptions options) : DbContext(options)
{
    public abstract string Schema { get; }

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected sealed override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyOutbox();
        ConfigureModel(modelBuilder);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) => configurationBuilder.UseUtcTimestamps();

    protected abstract void ConfigureModel(ModelBuilder modelBuilder);

    /// <summary>
    /// Decides which domain events other modules may see. Anything not mapped stays private to the module.
    /// </summary>
    protected virtual IEnumerable<IIntegrationEvent> ToIntegrationEvents(IDomainEvent domainEvent) => [];

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        DomainEventOutbox.Collect(this, ToIntegrationEvents);
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        throw new NotSupportedException("Use SaveChangesAsync so the outbox is always written.");
}

public static class DomainEventOutbox
{
    /// <summary>Moves pending domain events from tracked aggregates into outbox rows on <paramref name="db"/>.</summary>
    public static void Collect(DbContext db, Func<IDomainEvent, IEnumerable<IIntegrationEvent>> translate)
    {
        var aggregates = db.ChangeTracker.Entries<IHasDomainEvents>()
            .Select(e => e.Entity)
            .Where(a => a.DomainEvents.Count > 0)
            .ToList();

        foreach (var aggregate in aggregates)
        {
            foreach (var integrationEvent in aggregate.DomainEvents.SelectMany(translate))
            {
                db.Set<OutboxMessage>().Add(OutboxMessage.From(integrationEvent));
            }

            aggregate.ClearDomainEvents();
        }
    }
}
