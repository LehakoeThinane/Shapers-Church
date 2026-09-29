using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shapers.Church.Application;
using Shapers.Church.Contracts;
using Shapers.Platform;
using Shapers.Platform.Authorization;

namespace Shapers.Church.Infrastructure;

public static class ChurchInfrastructure
{
    public static IServiceCollection AddChurchInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<ChurchDbContext>(configuration, ChurchDbContext.SchemaName, typeof(IChurchDirectory).Assembly);
        services.AddScoped<IChurchDb>(sp => sp.GetRequiredService<ChurchDbContext>());
        services.AddScoped<IChurchDirectory, ChurchDirectory>();
        services.AddScoped<ChurchService>();
        services.AddScoped<ChurchSeeder>();
        services.AddSingleton<IPermissionProvider, ChurchPermissionProvider>();
        services.Configure<ChurchOptions>(configuration.GetSection(ChurchOptions.SectionName));
        return services;
    }

    public static async Task InitialiseChurchAsync(this IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ChurchDbContext>();
        await db.Database.MigrateAsync(cancellationToken);

        var environment = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<ChurchOptions>>().Value;
        await scope.ServiceProvider.GetRequiredService<ChurchSeeder>()
            .SeedAsync(options, includeDemoData: environment.IsDevelopment(), cancellationToken);
    }
}
