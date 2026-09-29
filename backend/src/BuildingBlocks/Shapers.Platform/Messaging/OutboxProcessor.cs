using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Shapers.Platform.Messaging;

/// <summary>
/// Polls one module's outbox and delivers pending messages. Rows are claimed with
/// <c>FOR UPDATE SKIP LOCKED</c>, so several API instances can run this safely side by side.
/// </summary>
public sealed partial class OutboxProcessor<TContext>(
    IServiceScopeFactory scopeFactory,
    IntegrationEventDispatcher dispatcher,
    TimeProvider clock,
    ILogger<OutboxProcessor<TContext>> logger) : BackgroundService
    where TContext : DbContext
{
    private const int BatchSize = 20;
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            int processed;
            try
            {
                processed = await ProcessBatchAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                LogBatchFailed(logger, typeof(TContext).Name, ex);
                processed = 0;
            }

            if (processed == 0)
            {
                await Task.Delay(IdleDelay, clock, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
    }

    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();

        var entity = db.Model.FindEntityType(typeof(OutboxMessage))!;
        var table = $"\"{entity.GetSchema()}\".\"{entity.GetTableName()}\"";

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

#pragma warning disable EF1003 // The table name comes from the EF model, not from user input.
        var messages = await db.Set<OutboxMessage>()
            .FromSqlRaw(
                $"SELECT * FROM {table} WHERE processed_at IS NULL AND attempts < {OutboxMessage.MaxAttempts} " +
                $"ORDER BY occurred_at LIMIT {BatchSize} FOR UPDATE SKIP LOCKED")
            .ToListAsync(cancellationToken);
#pragma warning restore EF1003

        foreach (var message in messages)
        {
            try
            {
                await dispatcher.DispatchAsync(message.Type, message.Payload, cancellationToken);
                message.MarkProcessed(clock.GetUtcNow());
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                message.MarkFailed(ex.ToString());
                LogMessageFailed(logger, message.Type, message.Id, message.Attempts, ex);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return messages.Count;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox batch for {Context} failed")]
    private static partial void LogBatchFailed(ILogger logger, string context, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Delivering {EventType} {MessageId} failed (attempt {Attempts})")]
    private static partial void LogMessageFailed(ILogger logger, string eventType, Guid messageId, int attempts, Exception exception);
}

/// <summary>Recurring job: removes delivered outbox rows once they are no longer useful for troubleshooting.</summary>
public sealed class OutboxCleanupJob<TContext>(TContext db, TimeProvider clock)
    where TContext : DbContext
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(14);

    public Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var cutoff = clock.GetUtcNow() - Retention;
        return db.Set<OutboxMessage>()
            .Where(m => m.ProcessedAt != null && m.ProcessedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
