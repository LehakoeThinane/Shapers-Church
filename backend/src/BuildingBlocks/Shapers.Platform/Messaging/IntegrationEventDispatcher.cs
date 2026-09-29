using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shapers.SharedKernel;

namespace Shapers.Platform.Messaging;

public interface IIntegrationEventHandler<in TEvent>
    where TEvent : IIntegrationEvent
{
    Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken);
}

/// <summary>
/// Remembers which handler has already processed which event, so redelivery after a partial failure
/// only re-runs the handlers that failed. Delivery is still at-least-once: handlers must be idempotent.
/// </summary>
public interface IInbox
{
    Task<bool> HasProcessedAsync(Guid eventId, string handler, CancellationToken cancellationToken);

    Task MarkProcessedAsync(Guid eventId, string handler, CancellationToken cancellationToken);
}

public sealed partial class IntegrationEventDispatcher(
    IServiceScopeFactory scopeFactory,
    IntegrationEventTypeRegistry registry,
    ILogger<IntegrationEventDispatcher> logger)
{
    private static readonly MethodInfo InvokeMethod =
        typeof(IntegrationEventDispatcher).GetMethod(nameof(InvokeAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    public async Task DispatchAsync(string typeName, string payload, CancellationToken cancellationToken)
    {
        var eventType = registry.Resolve(typeName);
        var integrationEvent = (IIntegrationEvent)(JsonSerializer.Deserialize(payload, eventType, MessagingJson.Options)
            ?? throw new InvalidOperationException($"Payload for '{typeName}' deserialised to null."));

        var handlerType = typeof(IIntegrationEventHandler<>).MakeGenericType(eventType);
        var invoke = InvokeMethod.MakeGenericMethod(eventType);

        await using var scope = scopeFactory.CreateAsyncScope();
        var inbox = scope.ServiceProvider.GetRequiredService<IInbox>();

        foreach (var handler in scope.ServiceProvider.GetServices(handlerType))
        {
            var handlerName = handler!.GetType().FullName!;
            if (await inbox.HasProcessedAsync(integrationEvent.EventId, handlerName, cancellationToken))
            {
                continue;
            }

            await (Task)invoke.Invoke(null, BindingFlags.DoNotWrapExceptions, null, [handler, integrationEvent, cancellationToken], null)!;
            await inbox.MarkProcessedAsync(integrationEvent.EventId, handlerName, cancellationToken);
            LogHandled(logger, typeName, handlerName, integrationEvent.EventId);
        }
    }

    private static Task InvokeAsync<TEvent>(object handler, IIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent =>
        ((IIntegrationEventHandler<TEvent>)handler).HandleAsync((TEvent)integrationEvent, cancellationToken);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{EventType} {EventId} handled by {Handler}")]
    private static partial void LogHandled(ILogger logger, string eventType, string handler, Guid eventId);
}
