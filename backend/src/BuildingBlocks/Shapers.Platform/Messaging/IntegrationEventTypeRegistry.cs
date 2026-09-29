using System.Reflection;
using Shapers.SharedKernel;

namespace Shapers.Platform.Messaging;

/// <summary>Registers a Contracts assembly whose integration events can travel through the outbox.</summary>
public sealed record IntegrationEventAssembly(Assembly Assembly);

/// <summary>Maps the stored event name back to its CLR type when a message is dispatched.</summary>
public sealed class IntegrationEventTypeRegistry
{
    private readonly Dictionary<string, Type> _types;

    public IntegrationEventTypeRegistry(IEnumerable<IntegrationEventAssembly> assemblies)
    {
        _types = assemblies
            .SelectMany(a => a.Assembly.GetTypes())
            .Where(t => t is { IsAbstract: false, IsInterface: false } && typeof(IIntegrationEvent).IsAssignableFrom(t))
            .ToDictionary(NameOf);
    }

    /// <summary>
    /// The stable name stored in the outbox. Renaming or moving an event type is a breaking change
    /// for messages already in flight.
    /// </summary>
    public static string NameOf(Type type) => type.FullName ?? type.Name;

    public Type Resolve(string name) =>
        _types.TryGetValue(name, out var type)
            ? type
            : throw new InvalidOperationException($"Integration event type '{name}' is not registered.");
}
