using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shapers.Giving.Application;
using Shapers.Giving.Contracts;
using Shapers.Platform;
using Shapers.Platform.Authorization;
using Shapers.Platform.Jobs;

namespace Shapers.Giving.Infrastructure;

public static class GivingInfrastructure
{
    public static IServiceCollection AddGivingInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<GivingDbContext>(configuration, GivingDbContext.SchemaName, typeof(GivingPermissions).Assembly);
        services.AddScoped<IGivingDb>(sp => sp.GetRequiredService<GivingDbContext>());
        services.AddSingleton<IPermissionProvider, GivingPermissionProvider>();
        services.Configure<GivingOptions>(configuration.GetSection(GivingOptions.SectionName));
        services.Configure<YocoOptions>(configuration.GetSection(YocoOptions.SectionName));

        // The provider is chosen in configuration (Giving:Provider); "None" keeps card giving on the church's payment link.
        if (string.Equals(configuration[$"{GivingOptions.SectionName}:Provider"], "Yoco", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient<IPaymentProvider, YocoPaymentProvider>((sp, c) =>
            {
                c.BaseAddress = new Uri(sp.GetRequiredService<IOptions<YocoOptions>>().Value.BaseUrl);
                c.Timeout = TimeSpan.FromSeconds(30);
            });
        }
        else
        {
            services.AddSingleton<IPaymentProvider, NoPaymentProvider>();
        }

        services.AddScoped<GivingService>();
        services.AddScoped<GivingAdminService>();
        services.AddScoped<GivingSeeder>();
        services.AddScoped<GivingRetentionJob>();
        services.AddScoped<Shapers.Platform.Privacy.IPersonalDataSource, GivingPersonalData>();

        services.AddSingleton(new RecurringJobDefinition("giving-retention", "35 2 * * *", (sp, ct) =>
            sp.GetRequiredService<GivingRetentionJob>().RunAsync(ct)));
        return services;
    }

    public static async Task InitialiseGivingAsync(this IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<GivingDbContext>().Database.MigrateAsync(cancellationToken);
        await scope.ServiceProvider.GetRequiredService<GivingSeeder>().SeedAsync(cancellationToken);
    }
}
