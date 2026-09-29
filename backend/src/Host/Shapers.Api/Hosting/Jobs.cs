using Hangfire;
using Hangfire.Dashboard;
using Hangfire.PostgreSql;
using Shapers.Platform;
using Shapers.Platform.Authorization;
using Shapers.Platform.Jobs;

namespace Shapers.Api.Hosting;

internal static class Jobs
{
    public static IServiceCollection AddShapersJobs(this IServiceCollection services, IConfiguration configuration)
    {
        if (!configuration.GetValue("Jobs:Enabled", true))
        {
            return services;
        }

        var connection = configuration.GetConnectionString(PlatformServiceCollectionExtensions.ConnectionStringName)!;
        services.AddHangfire(c => c
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(o => o.UseNpgsqlConnection(connection), new PostgreSqlStorageOptions { SchemaName = "jobs" }));
        services.AddHangfireServer(o => o.WorkerCount = Math.Min(Environment.ProcessorCount, 4));
        return services;
    }

    public static WebApplication UseShapersJobs(this WebApplication app)
    {
        if (!app.Configuration.GetValue("Jobs:Enabled", true))
        {
            return app;
        }

        var runner = app.Services.GetRequiredService<RecurringJobRunner>();
        var manager = app.Services.GetRequiredService<IRecurringJobManager>();
        foreach (var job in runner.Definitions)
        {
            var id = job.Id;
            manager.AddOrUpdate<RecurringJobRunner>(id, r => r.RunAsync(id, CancellationToken.None), job.Cron, new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
        }

        app.UseHangfireDashboard("/jobs", new DashboardOptions
        {
            DashboardTitle = "Shapers background jobs",
            AsyncAuthorization = [new PermissionDashboardFilter()],
            DisplayStorageConnectionString = false,
        });
        return app;
    }

    private sealed class PermissionDashboardFilter : IDashboardAsyncAuthorizationFilter
    {
        public Task<bool> AuthorizeAsync(DashboardContext context) =>
            context.GetHttpContext().RequestServices.GetRequiredService<IAuthorizer>().HasAnywhereAsync(PlatformPermissions.JobsView);
    }
}
