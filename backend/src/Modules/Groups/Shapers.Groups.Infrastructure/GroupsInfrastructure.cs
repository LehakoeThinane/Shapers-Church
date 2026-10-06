using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shapers.Groups.Application;
using Shapers.Groups.Contracts;
using Shapers.People.Contracts;
using Shapers.Platform;
using Shapers.Platform.Authorization;
using Shapers.Platform.Jobs;
using Shapers.Platform.Messaging;

namespace Shapers.Groups.Infrastructure;

public static class GroupsInfrastructure
{
    public static IServiceCollection AddGroupsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<GroupsDbContext>(configuration, GroupsDbContext.SchemaName, typeof(CellVisitorsRecordedIntegrationEvent).Assembly);
        services.AddScoped<IGroupsDb>(sp => sp.GetRequiredService<GroupsDbContext>());
        services.AddSingleton<IPermissionProvider, GroupsPermissionProvider>();

        // Leaders follow the same two-step rule as anyone opening personal information.
        services.Configure<GroupsOptions>(o => o.RequireMfaForLeaders = configuration.GetValue("Auth:Security:RequireMfaForSensitivePermissions", true));

        services.AddScoped<CellAdminService>();
        services.AddScoped<CellReportsService>();
        services.AddScoped<CellLeaderService>();
        services.AddScoped<ChurchLessonService>();
        services.AddScoped<MyCellsService>();
        services.AddScoped<CellReportRetentionJob>();
        services.AddScoped<Shapers.Platform.Privacy.IPersonalDataSource, GroupsPersonalData>();
        services.AddScoped<IIntegrationEventHandler<PeopleMergedIntegrationEvent>, ReplaceMergedPerson>();

        services.AddSingleton(new RecurringJobDefinition("groups-report-retention", "30 2 * * *", (sp, ct) =>
            sp.GetRequiredService<CellReportRetentionJob>().RunAsync(ct)));
        return services;
    }

    public static async Task InitialiseGroupsAsync(this IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<GroupsDbContext>().Database.MigrateAsync(cancellationToken);
    }
}
