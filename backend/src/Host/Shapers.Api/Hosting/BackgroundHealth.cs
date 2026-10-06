using Hangfire;
using Shapers.Platform.Authorization;
using Shapers.Platform.Jobs;
using Shapers.Platform.Messaging;

namespace Shapers.Api.Hosting;

/// <summary>
/// Background work that fails quietly: outbox messages that are stuck or given up on, and jobs that failed after
/// all their retries. Shown to staff with the jobs permission and logged every few minutes for the alert
/// (infra/azure/main.bicep). Names and counts only; nothing about people.
/// </summary>
public sealed record BackgroundHealthDto(bool NeedsAttention, DateTimeOffset CheckedAt, IReadOnlyList<OutboxHealth> Outbox, JobsHealthDto? Jobs);

/// <summary>Null on <see cref="BackgroundHealthDto.Jobs"/> when background jobs are switched off (Jobs:Enabled).</summary>
public sealed record JobsHealthDto(bool Running, long Failed, IReadOnlyList<JobFailureDto> RecentFailures);

public sealed record JobFailureDto(string Job, DateTimeOffset? FailedAt, string? ErrorType);

internal sealed class BackgroundHealthCheck(IEnumerable<IOutboxMonitor> outboxes, IServiceProvider services, TimeProvider clock)
{
    /// <summary>The job server writes a heartbeat every 30 seconds; none for this long means it has stopped.</summary>
    private static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromMinutes(5);
    private const int ListLimit = 10;

    public async Task<BackgroundHealthDto> CheckAsync(CancellationToken cancellationToken)
    {
        var outbox = new List<OutboxHealth>();
        foreach (var monitor in outboxes)
        {
            outbox.Add(await monitor.CheckAsync(cancellationToken));
        }

        var jobs = CheckJobs();
        var needsAttention = outbox.Any(o => o.NeedsAttention) || jobs is { Running: false } or { Failed: > 0 };
        return new BackgroundHealthDto(needsAttention, clock.GetUtcNow(), [.. outbox.OrderBy(o => o.Module, StringComparer.Ordinal)], jobs);
    }

    private JobsHealthDto? CheckJobs()
    {
        if (services.GetService<JobStorage>() is not { } storage)
        {
            return null;
        }

        var monitoring = storage.GetMonitoringApi();
        var now = clock.GetUtcNow();
        var running = monitoring.Servers().Any(s => s.Heartbeat is { } beat && now - new DateTimeOffset(beat, TimeSpan.Zero) < HeartbeatTimeout);
        var failures = monitoring.FailedJobs(0, ListLimit)
            .Select(j => new JobFailureDto(
                NameOf(j.Value.Job),
                j.Value.FailedAt is { } at ? new DateTimeOffset(at, TimeSpan.Zero) : null,
                OutboxProblem.ErrorTypeOf(j.Value.ExceptionType)))
            .ToList();
        return new JobsHealthDto(running, monitoring.FailedCount(), failures);
    }

    /// <summary>Recurring jobs all run through <see cref="RecurringJobRunner"/>; their name is the first argument.</summary>
    private static string NameOf(Hangfire.Common.Job? job) =>
        job is { Type: var type, Args: [string id, ..] } && type == typeof(RecurringJobRunner) ? id : job?.Method.Name ?? "Unknown job";
}

/// <summary>Logs a warning every few minutes while background work needs attention; the alert looks for it.</summary>
internal sealed partial class BackgroundHealthReporter(IServiceScopeFactory scopeFactory, TimeProvider clock, ILogger<BackgroundHealthReporter> logger)
    : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(Interval, clock, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var health = await scope.ServiceProvider.GetRequiredService<BackgroundHealthCheck>().CheckAsync(stoppingToken);
                if (health.NeedsAttention)
                {
                    LogNeedsAttention(
                        logger,
                        health.Outbox.Sum(o => o.Waiting),
                        health.Outbox.Sum(o => o.GaveUp),
                        health.Jobs?.Failed ?? 0,
                        health.Jobs?.Running ?? true);
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                LogCheckFailed(logger, ex);
            }
        }
    }

    // The alert in infra/azure/main.bicep matches the start of this message; keep the two in step.
    [LoggerMessage(Level = LogLevel.Warning, Message = "Background work needs attention: {WaitingMessages} messages waiting, {GaveUpMessages} given up, {FailedJobs} failed jobs, job server running: {JobServerRunning}")]
    private static partial void LogNeedsAttention(ILogger logger, int waitingMessages, int gaveUpMessages, long failedJobs, bool jobServerRunning);

    [LoggerMessage(Level = LogLevel.Error, Message = "Checking background work failed")]
    private static partial void LogCheckFailed(ILogger logger, Exception exception);
}

internal static class BackgroundHealth
{
    public static IServiceCollection AddBackgroundHealth(this IServiceCollection services)
    {
        services.AddScoped<BackgroundHealthCheck>();
        services.AddHostedService<BackgroundHealthReporter>();
        return services;
    }

    public static IEndpointRouteBuilder MapBackgroundHealth(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/background-health", async (BackgroundHealthCheck check, CancellationToken cancellationToken) =>
                TypedResults.Ok(await check.CheckAsync(cancellationToken)))
            .WithTags("Background work")
            .WithName("GetBackgroundHealth")
            .RequirePermission(PlatformPermissions.JobsView);
        return endpoints;
    }
}
