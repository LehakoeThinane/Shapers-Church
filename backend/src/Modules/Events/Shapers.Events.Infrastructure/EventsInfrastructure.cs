using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shapers.Events.Application;
using Shapers.Events.Contracts;
using Shapers.Platform;
using Shapers.Platform.Authorization;
using Shapers.Platform.Jobs;
using Shapers.Platform.Messaging;

namespace Shapers.Events.Infrastructure;

public static class EventsInfrastructure
{
    public static IServiceCollection AddEventsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<EventsDbContext>(configuration, EventsDbContext.SchemaName, typeof(EventRegisteredIntegrationEvent).Assembly);
        services.AddScoped<IEventsDb>(sp => sp.GetRequiredService<EventsDbContext>());
        services.Configure<EventsOptions>(configuration.GetSection(EventsOptions.SectionName));
        services.AddSingleton<IPermissionProvider, EventsPermissionProvider>();

        services.AddScoped<EventReader>();
        services.AddScoped<EventEmails>();
        services.AddScoped<EventAdminService>();
        services.AddScoped<PublicEventService>();
        services.AddScoped<RegistrationService>();
        services.AddScoped<ReminderJob>();
        services.AddScoped<Shapers.Platform.Privacy.IPersonalDataSource, EventsPersonalData>();
        services.AddScoped<IIntegrationEventHandler<EventRegisteredIntegrationEvent>, ConfirmMemberRegistration>();
        services.AddScoped<IIntegrationEventHandler<WaitlistPromotedIntegrationEvent>, NotifyWaitlistPromotion>();
        services.AddScoped<IIntegrationEventHandler<EventCancelledIntegrationEvent>, NotifyEventCancelled>();

        services.AddSingleton(new RecurringJobDefinition("events-reminders", "5 * * * *", (sp, ct) =>
            sp.GetRequiredService<ReminderJob>().RunAsync(ct)));
        services.AddSingleton(new RecurringJobDefinition("events-verification-cleanup", "50 3 * * *", (sp, ct) =>
        {
            var cutoff = sp.GetRequiredService<TimeProvider>().GetUtcNow().AddDays(-2);
            return sp.GetRequiredService<EventsDbContext>().EmailVerifications.Where(v => v.CreatedAt < cutoff).ExecuteDeleteAsync(ct);
        }));
        return services;
    }

    public static async Task InitialiseEventsAsync(this IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<EventsDbContext>().Database.MigrateAsync(cancellationToken);
    }
}
