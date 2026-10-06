using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shapers.People.Application;
using Shapers.People.Contracts;
using Shapers.People.Domain;
using Shapers.Platform;
using Shapers.Platform.Authorization;
using Shapers.Platform.Jobs;

namespace Shapers.People.Infrastructure;

public static class PeopleInfrastructure
{
    public static IServiceCollection AddPeopleInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<PeopleDbContext>(configuration, PeopleDbContext.SchemaName, typeof(IPeopleDirectory).Assembly);
        services.AddScoped<IPeopleDb>(sp => sp.GetRequiredService<PeopleDbContext>());
        services.AddSingleton<IPermissionProvider, PeoplePermissionProvider>();

        services.AddScoped<PersonDetailBuilder>();
        services.AddScoped<DuplicateDetector>();
        services.AddScoped<PeopleService>();
        services.AddScoped<HouseholdService>();
        services.AddScoped<MergeService>();
        services.AddScoped<MyProfileService>();
        services.AddScoped<ConnectCardService>();
        services.AddScoped<Shapers.Platform.Messaging.IIntegrationEventHandler<Shapers.Groups.Contracts.CellVisitorsRecordedIntegrationEvent>, FileCellVisitors>();
        services.AddScoped<IGuestRecords, GuestRecords>();
        services.AddScoped<IPeopleDirectory, PeopleDirectory>();
        services.AddScoped<IPeopleRegistration, PeopleRegistration>();
        services.AddScoped<ConnectCardRetentionJob>();
        services.AddScoped<Shapers.Platform.Privacy.IPersonalDataSource, PeoplePersonalData>();
        services.AddSingleton(new RecurringJobDefinition("people-connect-card-retention", "30 2 * * *", (sp, ct) =>
            sp.GetRequiredService<ConnectCardRetentionJob>().RunAsync(ct)));
        return services;
    }

    public static async Task InitialisePeopleAsync(this IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PeopleDbContext>();
        await db.Database.MigrateAsync(cancellationToken);

        if (!await db.MembershipStatuses.AnyAsync(cancellationToken))
        {
            db.MembershipStatuses.AddRange(MembershipStatus.Defaults());
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
