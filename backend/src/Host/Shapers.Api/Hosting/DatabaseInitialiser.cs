using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Shapers.Platform.Modules;
using Shapers.Platform.Persistence;

namespace Shapers.Api.Hosting;

internal static class DatabaseInitialiser
{
    /// <summary>
    /// Applies migrations and reference data. Order matters: Church (organisation and campuses) before
    /// People (statuses) before Identity (roles and the first administrator, who needs a person and a scope).
    /// </summary>
    public static async Task InitialiseAsync(this WebApplication app, IReadOnlyList<IModule> modules, CancellationToken cancellationToken = default)
    {
        if (!app.Configuration.GetValue("Database:MigrateOnStartup", true))
        {
            return;
        }

        await using (var scope = app.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<PlatformDbContext>().Database.MigrateAsync(cancellationToken);
        }

        foreach (var module in modules)
        {
            await module.InitialiseAsync(app.Services, cancellationToken);
        }
    }
}

internal sealed class DatabaseHealthCheck(PlatformDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Cannot reach PostgreSQL.");
}
