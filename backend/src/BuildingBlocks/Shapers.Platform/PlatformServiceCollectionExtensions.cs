using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shapers.Platform.Auditing;
using Shapers.Platform.Authorization;
using Shapers.Platform.Jobs;
using Shapers.Platform.Messaging;
using Shapers.Platform.Persistence;
using Shapers.Platform.Security;

namespace Shapers.Platform;

public static class PlatformServiceCollectionExtensions
{
    public const string ConnectionStringName = "Shapers";

    public static IServiceCollection AddPlatform(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpContextAccessor();

        services.AddDbContext<PlatformDbContext>(o => o.UsePostgres(configuration, PlatformDbContext.Schema));
        services.AddScoped<IAuditLog, AuditLog>();
        services.AddScoped<IInbox, EfInbox>();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();

        services.AddSingleton<IPermissionProvider, PlatformPermissions.Provider>();
        services.AddSingleton<PermissionCatalog>();

        services.AddSingleton<IntegrationEventTypeRegistry>();
        services.AddSingleton<IntegrationEventDispatcher>();
        services.AddSingleton<RecurringJobRunner>();
        services.AddSingleton<IKeyedHasher, KeyedHasher>();

        services.AddSingleton(new RecurringJobDefinition("platform-inbox-cleanup", "30 3 * * *", (sp, ct) =>
        {
            var cutoff = sp.GetRequiredService<TimeProvider>().GetUtcNow().AddDays(-30);
            return sp.GetRequiredService<PlatformDbContext>().InboxRecords.Where(r => r.ProcessedAt < cutoff).ExecuteDeleteAsync(ct);
        }));

        // The audit log is kept for five years (the church's retention schedule), then deleted. The database refuses
        // to delete anything younger, so this is the only way entries ever leave.
        services.AddSingleton(new RecurringJobDefinition("platform-audit-retention", "0 4 * * *", (sp, ct) =>
        {
            var cutoff = sp.GetRequiredService<TimeProvider>().GetUtcNow() - AuditLog.Retention;
            return sp.GetRequiredService<PlatformDbContext>().AuditEntries.Where(e => e.OccurredAt < cutoff).ExecuteDeleteAsync(ct);
        }));
        return services;
    }

    /// <summary>
    /// Registers a module's DbContext in its own schema, plus the background processor for its outbox
    /// and the Contracts assembly whose events it may publish.
    /// </summary>
    public static IServiceCollection AddModuleDbContext<TContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        string schema,
        Assembly contractsAssembly)
        where TContext : DbContext
    {
        services.AddDbContext<TContext>(o => o.UsePostgres(configuration, schema));
        services.AddSingleton(new IntegrationEventAssembly(contractsAssembly));
        services.AddScoped<OutboxCleanupJob<TContext>>();
        services.AddSingleton<OutboxProcessor<TContext>>();
        services.AddSingleton(new RecurringJobDefinition($"{schema}-outbox-cleanup", "0 3 * * *", (sp, ct) =>
            sp.GetRequiredService<OutboxCleanupJob<TContext>>().RunAsync(ct)));
        if (configuration.GetValue("Outbox:Enabled", true))
        {
            services.AddHostedService(sp => sp.GetRequiredService<OutboxProcessor<TContext>>());
        }

        return services;
    }

    public static DbContextOptionsBuilder UsePostgres(this DbContextOptionsBuilder options, IConfiguration configuration, string schema) =>
        options
            .UseNpgsql(
                configuration.GetConnectionString(ConnectionStringName)
                    ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is missing."),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", schema))
            .UseSnakeCaseNamingConvention();
}
