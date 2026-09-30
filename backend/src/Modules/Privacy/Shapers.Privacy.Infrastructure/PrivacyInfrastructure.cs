using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shapers.Platform;
using Shapers.Platform.Authorization;
using Shapers.Platform.Jobs;
using Shapers.Platform.Persistence;
using Shapers.Privacy.Application;
using Shapers.Privacy.Contracts;
using Shapers.Privacy.Domain;

namespace Shapers.Privacy.Infrastructure;

public sealed class PrivacyDbContext(DbContextOptions<PrivacyDbContext> options) : ModuleDbContext(options), IPrivacyDb
{
    public const string SchemaName = "privacy";

    public override string Schema => SchemaName;

    public DbSet<DataRequest> Requests => Set<DataRequest>();

    Task<int> IPrivacyDb.SaveChangesAsync(CancellationToken cancellationToken) => SaveChangesAsync(cancellationToken);

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DataRequest>(b =>
        {
            b.ToTable("data_requests");
            b.Property(r => r.Id).ValueGeneratedNever();
            b.Property(r => r.Type).HasConversion<string>().HasMaxLength(20);
            b.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(r => r.Details).HasMaxLength(DataRequest.MaxDetails);
            b.Property(r => r.Response).HasMaxLength(DataRequest.MaxDetails);
            b.HasIndex(r => new { r.Status, r.DueAt });
            b.HasIndex(r => r.PersonId);
            b.Property<uint>("xmin").IsRowVersion();
        });
    }

    protected override IEnumerable<IIntegrationEvent> ToIntegrationEvents(IDomainEvent domainEvent) => domainEvent switch
    {
        PersonErasureCompleted e => [new PersonErasedIntegrationEvent(e.PersonId, "request")],
        _ => [],
    };
}

public static class PrivacyInfrastructure
{
    public static IServiceCollection AddPrivacyInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<PrivacyDbContext>(configuration, PrivacyDbContext.SchemaName, typeof(PersonErasedIntegrationEvent).Assembly);
        services.AddScoped<IPrivacyDb>(sp => sp.GetRequiredService<PrivacyDbContext>());
        services.AddSingleton<IPermissionProvider, PrivacyPermissionProvider>();

        services.AddScoped<PersonalDataService>();
        services.AddScoped<MyPrivacyService>();
        services.AddScoped<DataRequestAdminService>();
        services.AddScoped<GuestRetentionJob>();
        services.AddSingleton(new RecurringJobDefinition("privacy-guest-retention", "40 2 * * *", (sp, ct) =>
            sp.GetRequiredService<GuestRetentionJob>().RunAsync(ct)));
        return services;
    }

    public static async Task InitialisePrivacyAsync(this IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PrivacyDbContext>().Database.MigrateAsync(cancellationToken);
    }
}
