using Microsoft.Extensions.DependencyInjection;

namespace Shapers.Platform.Jobs;

/// <summary>
/// A recurring background job declared by a module. The host registers every definition with Hangfire,
/// so modules don't depend on the scheduler directly.
/// </summary>
public sealed record RecurringJobDefinition(string Id, string Cron, Func<IServiceProvider, CancellationToken, Task> Run);

public sealed class RecurringJobRunner(IEnumerable<RecurringJobDefinition> definitions, IServiceScopeFactory scopeFactory)
{
    public IReadOnlyList<RecurringJobDefinition> Definitions { get; } = definitions.ToList();

    /// <summary>Invoked by Hangfire. Each run gets its own DI scope.</summary>
    public async Task RunAsync(string id, CancellationToken cancellationToken)
    {
        var definition = Definitions.SingleOrDefault(d => d.Id == id)
            ?? throw new InvalidOperationException($"Recurring job '{id}' is not registered.");
        await using var scope = scopeFactory.CreateAsyncScope();
        await definition.Run(scope.ServiceProvider, cancellationToken);
    }
}
