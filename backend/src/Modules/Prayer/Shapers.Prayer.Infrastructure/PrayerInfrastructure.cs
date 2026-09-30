using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shapers.People.Contracts;
using Shapers.Platform;
using Shapers.Platform.Authorization;
using Shapers.Platform.Jobs;
using Shapers.Platform.Messaging;
using Shapers.Prayer.Application;
using Shapers.Prayer.Contracts;

namespace Shapers.Prayer.Infrastructure;

public static class PrayerInfrastructure
{
    public static IServiceCollection AddPrayerInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<PrayerDbContext>(configuration, PrayerDbContext.SchemaName, typeof(PrayerRequestSubmittedIntegrationEvent).Assembly);
        services.AddScoped<IPrayerDb>(sp => sp.GetRequiredService<PrayerDbContext>());
        services.AddSingleton<IPermissionProvider, PrayerPermissionProvider>();

        services.AddScoped<PrayerService>();
        services.AddScoped<PrayerAdminService>();
        services.AddScoped<WallExpiryJob>();
        services.AddScoped<PrayerRetentionJob>();
        services.AddScoped<Shapers.Platform.Privacy.IPersonalDataSource, PrayerPersonalData>();
        services.AddScoped<IIntegrationEventHandler<ConnectCardSubmittedIntegrationEvent>, FileConnectCardPrayer>();

        services.AddSingleton(new RecurringJobDefinition("prayer-wall-expiry", "15 2 * * *", (sp, ct) =>
            sp.GetRequiredService<WallExpiryJob>().RunAsync(ct)));
        services.AddSingleton(new RecurringJobDefinition("prayer-retention", "20 2 * * *", (sp, ct) =>
            sp.GetRequiredService<PrayerRetentionJob>().RunAsync(ct)));
        return services;
    }

    public static async Task InitialisePrayerAsync(this IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PrayerDbContext>().Database.MigrateAsync(cancellationToken);
    }
}
