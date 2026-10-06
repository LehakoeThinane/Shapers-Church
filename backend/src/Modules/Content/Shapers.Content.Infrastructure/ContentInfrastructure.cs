using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shapers.Content.Application;
using Shapers.Content.Contracts;
using Shapers.Content.Domain;
using Shapers.Media.Contracts;
using Shapers.Platform;
using Shapers.Platform.Authorization;
using Shapers.Platform.Jobs;
using Shapers.Platform.Messaging;
using Shapers.Platform.Persistence;

namespace Shapers.Content.Infrastructure;

public sealed class ContentDbContext(DbContextOptions<ContentDbContext> options) : ModuleDbContext(options), IContentDb
{
    public const string SchemaName = "content";

    public override string Schema => SchemaName;

    public DbSet<Page> Pages => Set<Page>();

    public DbSet<Post> Posts => Set<Post>();

    Task<int> IContentDb.SaveChangesAsync(CancellationToken cancellationToken) => SaveChangesAsync(cancellationToken);

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Page>(b =>
        {
            b.ToTable("pages");
            Common(b);
        });

        modelBuilder.Entity<Post>(b =>
        {
            b.ToTable("posts");
            Common(b);
            b.Property(p => p.Kind).HasConversion<string>().HasMaxLength(20);
            b.Property(p => p.Author).HasMaxLength(100);
            b.Property(p => p.CoverImageUrl).HasMaxLength(500);
            b.HasIndex(p => new { p.Kind, p.Status, p.PublishedAt });
        });
    }

    protected override IEnumerable<IIntegrationEvent> ToIntegrationEvents(IDomainEvent domainEvent) => domainEvent switch
    {
        ContentPublished e => [new ContentPublishedIntegrationEvent(e.Id, e.Kind, e.Slug)],
        _ => [],
    };

    private static void Common<T>(EntityTypeBuilder<T> b)
        where T : PublishableContent
    {
        b.Property(p => p.Id).ValueGeneratedNever();
        b.Property(p => p.Title).HasMaxLength(PublishableContent.MaxTitle);
        b.Property(p => p.Slug).HasMaxLength(Slug.MaxLength);
        b.Property(p => p.Summary).HasMaxLength(PublishableContent.MaxSummary);
        b.Property(p => p.Body).HasMaxLength(PublishableContent.MaxBody);
        b.Property(p => p.Scope).HasMaxLength(512);
        b.Property(p => p.LegacyPath).HasMaxLength(300);
        b.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(p => p.Language).HasMaxLength(5).HasDefaultValue(Languages.English);
        b.Ignore(p => p.IsTranslation);
        b.Ignore(p => p.TranslationChecked);
        // A translation shares its original's address, so an address is unique per language.
        b.HasIndex(p => new { p.Slug, p.Language }).IsUnique();
        b.HasIndex(p => new { p.TranslationOfId, p.Language }).IsUnique().HasFilter("translation_of_id IS NOT NULL");
        b.HasIndex(p => new { p.Status, p.PublishAt });
        b.Property<uint>("xmin").IsRowVersion();
    }
}

public static class ContentInfrastructure
{
    public static IServiceCollection AddContentInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<ContentDbContext>(configuration, ContentDbContext.SchemaName, typeof(ContentPublishedIntegrationEvent).Assembly);
        services.AddScoped<IContentDb>(sp => sp.GetRequiredService<ContentDbContext>());
        services.AddSingleton<IPermissionProvider, ContentPermissionProvider>();
        services.Configure<SiteRebuildOptions>(configuration.GetSection(SiteRebuildOptions.SectionName));
        services.AddHttpClient("site-rebuild", c => c.Timeout = TimeSpan.FromSeconds(15));
        services.Configure<WordPressOptions>(configuration.GetSection(WordPressOptions.SectionName));
        services.AddHttpClient<IWordPressSource, WordPressHttpSource>((sp, c) =>
        {
            var site = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<WordPressOptions>>().Value.SiteUrl;
            c.BaseAddress = new Uri(site.TrimEnd('/') + "/");
            c.Timeout = TimeSpan.FromSeconds(60);
        });
        services.AddScoped<WordPressImporter>();

        services.AddScoped<ContentAdminService>();
        services.AddScoped<PublicContentService>();
        services.AddScoped<IContentSource, ContentSource>();
        services.AddScoped<ContentPublisherJob>();
        services.AddScoped<IIntegrationEventHandler<ContentPublishedIntegrationEvent>, RebuildSiteOnPublish>();
        services.AddScoped<IIntegrationEventHandler<SermonPublishedIntegrationEvent>, RebuildSiteOnPublish>();
        services.AddSingleton(new RecurringJobDefinition("content-publish-scheduled", "* * * * *", (sp, ct) =>
            sp.GetRequiredService<ContentPublisherJob>().RunAsync(ct)));
        return services;
    }

    public static async Task InitialiseContentAsync(this IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ContentDbContext>().Database.MigrateAsync(cancellationToken);
    }
}
