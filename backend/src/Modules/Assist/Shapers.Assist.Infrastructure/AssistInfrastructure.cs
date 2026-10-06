using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shapers.Assist.Application;
using Shapers.Assist.Contracts;
using Shapers.Platform;
using Shapers.Platform.Authorization;
using Shapers.Platform.Jobs;

namespace Shapers.Assist.Infrastructure;

public static class AssistInfrastructure
{
    public static IServiceCollection AddAssistInfrastructure(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddModuleDbContext<AssistDbContext>(configuration, AssistDbContext.SchemaName, typeof(AssistPermissions).Assembly);
        services.AddScoped<IAssistDb>(sp => sp.GetRequiredService<AssistDbContext>());
        services.AddSingleton<IPermissionProvider, AssistPermissionProvider>();
        services.Configure<AssistOptions>(configuration.GetSection(AssistOptions.SectionName));

        var provider = configuration[$"{AssistOptions.SectionName}:Provider"] ?? "Disabled";
        if (provider.Equals("Azure", StringComparison.OrdinalIgnoreCase))
        {
            // Transcribing an hour of audio can take a few minutes.
            services.AddHttpClient(AzureAiProvider.HttpClientName, c => c.Timeout = TimeSpan.FromMinutes(10));
            services.AddSingleton<IAiProvider, AzureAiProvider>();
        }
        else if (provider.Equals("Fake", StringComparison.OrdinalIgnoreCase))
        {
            if (environment.IsProduction())
            {
                throw new InvalidOperationException("Assist:Provider 'Fake' is for development, tests and demos only.");
            }

            services.AddSingleton<FakeAiProvider>();
            services.AddSingleton<IAiProvider>(sp => sp.GetRequiredService<FakeAiProvider>());
        }
        else if (provider.Equals("Disabled", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IAiProvider, DisabledAiProvider>();
        }
        else
        {
            throw new InvalidOperationException($"Unknown Assist:Provider '{provider}'. Use Disabled, Azure or Fake.");
        }

        services.AddScoped<AiGateway>();
        services.AddScoped<IAssistTranscriber, AssistTranscriber>();
        services.AddScoped<DraftService>();
        services.AddScoped<UsageService>();
        services.AddScoped<AssistRetentionJob>();
        services.AddSingleton(new RecurringJobDefinition("assist-retention", "40 2 * * *", (sp, ct) =>
            sp.GetRequiredService<AssistRetentionJob>().RunAsync(ct)));
        return services;
    }

    public static async Task InitialiseAssistAsync(this IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AssistDbContext>().Database.MigrateAsync(cancellationToken);
    }
}
