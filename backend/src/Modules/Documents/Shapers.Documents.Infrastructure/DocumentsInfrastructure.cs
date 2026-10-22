using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shapers.Documents.Application;
using Shapers.Documents.Contracts;
using Shapers.Platform;
using Shapers.Platform.Authorization;
using Shapers.Platform.Jobs;

namespace Shapers.Documents.Infrastructure;

public static class DocumentsInfrastructure
{
    public static IServiceCollection AddDocumentsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<DocumentsDbContext>(configuration, DocumentsDbContext.SchemaName, typeof(DocumentsPermissions).Assembly);
        services.AddScoped<IDocumentsDb>(sp => sp.GetRequiredService<DocumentsDbContext>());
        services.AddSingleton<IPermissionProvider, DocumentsPermissionProvider>();

        services.AddScoped<DocumentsService>();
        services.AddScoped<DocumentsRecycleJob>();
        services.AddScoped<Shapers.Platform.Privacy.IPersonalDataSource, DocumentsPersonalData>();

        services.AddSingleton(new RecurringJobDefinition("documents-recycle-bin", "40 2 * * *", (sp, ct) =>
            sp.GetRequiredService<DocumentsRecycleJob>().RunAsync(ct)));
        return services;
    }

    public static async Task InitialiseDocumentsAsync(this IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DocumentsDbContext>().Database.MigrateAsync(cancellationToken);
    }
}
