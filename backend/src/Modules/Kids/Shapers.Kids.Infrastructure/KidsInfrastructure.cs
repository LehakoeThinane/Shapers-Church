using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shapers.Kids.Application;
using Shapers.Kids.Contracts;
using Shapers.Platform;
using Shapers.Platform.Authorization;
using Shapers.Platform.Jobs;

namespace Shapers.Kids.Infrastructure;

public static class KidsInfrastructure
{
    public static IServiceCollection AddKidsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<KidsDbContext>(configuration, KidsDbContext.SchemaName, typeof(KidsPermissions).Assembly);
        services.AddScoped<IKidsDb>(sp => sp.GetRequiredService<KidsDbContext>());
        services.AddSingleton<IPermissionProvider, KidsPermissionProvider>();

        services.AddScoped<ClassFinder>();
        services.AddScoped<CheckInWriter>();
        services.AddScoped<ParentKidsService>();
        services.AddScoped<KidsDeskService>();
        services.AddScoped<KidsClassService>();
        services.AddScoped<KidsRetentionJob>();
        services.AddScoped<Shapers.Platform.Privacy.IPersonalDataSource, KidsPersonalData>();

        services.AddSingleton(new RecurringJobDefinition("kids-retention", "25 2 * * *", (sp, ct) =>
            sp.GetRequiredService<KidsRetentionJob>().RunAsync(ct)));
        return services;
    }

    public static async Task InitialiseKidsAsync(this IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KidsDbContext>().Database.MigrateAsync(cancellationToken);
        await scope.ServiceProvider.GetRequiredService<KidsClassService>().SeedDefaultsAsync(cancellationToken);
    }
}
