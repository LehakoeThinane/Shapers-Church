using Microsoft.EntityFrameworkCore;

namespace Shapers.Platform.Messaging;

/// <summary>One module's outbox messages that need someone's attention.</summary>
public sealed record OutboxHealth(string Module, int Waiting, int GaveUp, IReadOnlyList<OutboxProblem> Oldest)
{
    public bool NeedsAttention => Waiting > 0 || GaveUp > 0;
}

/// <summary>
/// An undelivered message. Only the exception type is shown: error messages (from the database, for example)
/// can contain people's details.
/// </summary>
public sealed record OutboxProblem(Guid Id, string EventType, DateTimeOffset OccurredAt, int Attempts, bool GaveUp, string? ErrorType)
{
    /// <summary>"System.InvalidOperationException: details…" becomes "InvalidOperationException".</summary>
    public static string? ErrorTypeOf(string? lastError)
    {
        if (string.IsNullOrWhiteSpace(lastError))
        {
            return null;
        }

        // Exception.ToString() starts with the type's full name; stop before anything that follows it.
        var trimmed = lastError.TrimStart();
        var end = trimmed.IndexOfAny([':', ' ', '(', '\r', '\n']);
        var fullName = end < 0 ? trimmed : trimmed[..end];
        return fullName[(fullName.LastIndexOf('.') + 1)..];
    }
}

public interface IOutboxMonitor
{
    Task<OutboxHealth> CheckAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Reads one module's own outbox, like <see cref="OutboxProcessor{TContext}"/>, for messages still waiting after
/// <see cref="StuckAfter"/> and messages given up on after <see cref="OutboxMessage.MaxAttempts"/> attempts.
/// Nothing else reports either, so without this they go unnoticed.
/// </summary>
public sealed class OutboxMonitor<TContext>(TContext db, string module, TimeProvider clock) : IOutboxMonitor
    where TContext : DbContext
{
    public static readonly TimeSpan StuckAfter = TimeSpan.FromMinutes(10);
    private const int ListLimit = 10;

    public async Task<OutboxHealth> CheckAsync(CancellationToken cancellationToken)
    {
        var stuckBefore = clock.GetUtcNow() - StuckAfter;
        var problems = db.Set<OutboxMessage>().AsNoTracking()
            .Where(m => m.ProcessedAt == null && (m.Attempts >= OutboxMessage.MaxAttempts || m.OccurredAt < stuckBefore));

        var gaveUp = await problems.CountAsync(m => m.Attempts >= OutboxMessage.MaxAttempts, cancellationToken);
        var waiting = await problems.CountAsync(m => m.Attempts < OutboxMessage.MaxAttempts, cancellationToken);
        var oldest = waiting + gaveUp == 0
            ? []
            : await problems
                .OrderBy(m => m.OccurredAt)
                .Take(ListLimit)
                .Select(m => new { m.Id, m.Type, m.OccurredAt, m.Attempts, m.LastError })
                .ToListAsync(cancellationToken);

        return new OutboxHealth(
            module,
            waiting,
            gaveUp,
            [.. oldest.Select(m => new OutboxProblem(
                m.Id, m.Type, m.OccurredAt, m.Attempts, m.Attempts >= OutboxMessage.MaxAttempts, OutboxProblem.ErrorTypeOf(m.LastError)))]);
    }
}
