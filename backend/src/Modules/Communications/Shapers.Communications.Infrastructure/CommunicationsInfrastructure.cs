using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shapers.Communications.Application;
using Shapers.Events.Contracts;
using Shapers.Media.Contracts;
using Shapers.Platform;
using Shapers.Platform.Authorization;
using Shapers.Platform.Jobs;
using Shapers.Platform.Messaging;
using Shapers.Prayer.Contracts;

namespace Shapers.Communications.Infrastructure;

public static class CommunicationsInfrastructure
{
    public static IServiceCollection AddCommunicationsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<CommunicationsDbContext>(configuration, CommunicationsDbContext.SchemaName, typeof(Shapers.Communications.Contracts.CommunicationsPermissions).Assembly);
        services.AddScoped<ICommunicationsDb>(sp => sp.GetRequiredService<CommunicationsDbContext>());
        services.Configure<PushOptions>(configuration.GetSection(PushOptions.SectionName));
        services.AddSingleton<IPermissionProvider, CommunicationsPermissionProvider>();

        if (string.Equals(configuration[$"{PushOptions.SectionName}:Provider"], "Log", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IPushSender, LoggingPushSender>();
        }
        else
        {
            services.AddHttpClient<IPushSender, ExpoPushSender>(c =>
            {
                c.BaseAddress = new Uri("https://exp.host/--/api/v2/");
                c.Timeout = TimeSpan.FromSeconds(30);
            });
        }

        services.AddScoped<Notifier>();
        services.AddScoped<MemberNotifications>();
        services.AddScoped<DeliveryJob>();
        services.AddScoped<DeliveryLog>();
        services.AddScoped<IIntegrationEventHandler<LivestreamStartedIntegrationEvent>, NotifyLivestreamStarted>();
        services.AddScoped<IIntegrationEventHandler<SermonPublishedIntegrationEvent>, NotifySermonPublished>();
        services.AddScoped<IIntegrationEventHandler<WaitlistPromotedIntegrationEvent>, NotifyWaitlistPromoted>();
        services.AddScoped<IIntegrationEventHandler<PrayerRequestApprovedIntegrationEvent>, NotifyPrayerApproved>();

        services.AddSingleton(new RecurringJobDefinition("communications-deliver", "* * * * *", (sp, ct) =>
            sp.GetRequiredService<DeliveryJob>().RunAsync(ct)));
        return services;
    }

    public static async Task InitialiseCommunicationsAsync(this IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<CommunicationsDbContext>().Database.MigrateAsync(cancellationToken);
    }
}
