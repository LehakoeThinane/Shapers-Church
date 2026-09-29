namespace Shapers.SharedKernel;

/// <summary>
/// Something that happened inside a module. Domain events never leave their module directly;
/// the module decides which of them become integration events.
/// </summary>
public interface IDomainEvent;

/// <summary>
/// A fact published to other modules through the transactional outbox.
/// Integration events live in a module's Contracts project and must stay backwards compatible.
/// </summary>
public interface IIntegrationEvent
{
    Guid EventId { get; }

    DateTimeOffset OccurredAt { get; }
}

public abstract record IntegrationEvent : IIntegrationEvent
{
    public Guid EventId { get; init; } = Guid.CreateVersion7();

    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}
