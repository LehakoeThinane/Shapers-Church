using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shapers.People.Contracts;
using Shapers.Platform;
using Shapers.Platform.Authorization;
using Shapers.Platform.Jobs;
using Shapers.Platform.Messaging;
using Shapers.Platform.Persistence;
using Shapers.Services.Application;
using Shapers.Services.Contracts;
using Shapers.Services.Domain;

namespace Shapers.Services.Infrastructure;

public sealed class ServicesDbContext(DbContextOptions<ServicesDbContext> options) : ModuleDbContext(options), IServicesDb
{
    public const string SchemaName = "services";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public override string Schema => SchemaName;

    public DbSet<TeamCategory> Categories => Set<TeamCategory>();

    public DbSet<Team> Teams => Set<Team>();

    public DbSet<TeamPosition> Positions => Set<TeamPosition>();

    public DbSet<TeamMember> Members => Set<TeamMember>();

    public DbSet<Blockout> Blockouts => Set<Blockout>();

    public DbSet<Assignment> Assignments => Set<Assignment>();

    public DbSet<ServiceType> ServiceTypes => Set<ServiceType>();

    public DbSet<Plan> Plans => Set<Plan>();

    public DbSet<Song> Songs => Set<Song>();

    Task<int> IServicesDb.SaveChangesAsync(CancellationToken cancellationToken) => SaveChangesAsync(cancellationToken);

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TeamCategory>(b =>
        {
            b.ToTable("categories");
            b.Property(c => c.Id).ValueGeneratedNever();
            b.Property(c => c.Name).HasMaxLength(60);
            b.Property(c => c.Description).HasMaxLength(200);
            b.Property(c => c.Scope).HasMaxLength(512);
        });

        modelBuilder.Entity<Team>(b =>
        {
            b.ToTable("teams");
            b.HasIndex(t => t.CategoryId);
            b.Property(t => t.Id).ValueGeneratedNever();
            b.Property(t => t.Name).HasMaxLength(80);
            b.Property(t => t.Description).HasMaxLength(500);
            b.Property(t => t.Scope).HasMaxLength(512);
            b.HasIndex(t => t.Scope).HasOperators("text_pattern_ops");
        });

        modelBuilder.Entity<TeamPosition>(b =>
        {
            b.ToTable("positions");
            b.Property(p => p.Id).ValueGeneratedNever();
            b.Property(p => p.Name).HasMaxLength(60);
            b.HasIndex(p => p.TeamId);
        });

        modelBuilder.Entity<TeamMember>(b =>
        {
            b.ToTable("members");
            b.Property(m => m.Id).ValueGeneratedNever();
            b.HasIndex(m => new { m.TeamId, m.PersonId }).IsUnique();
            b.HasIndex(m => m.PersonId);
        });

        modelBuilder.Entity<Blockout>(b =>
        {
            b.ToTable("blockouts");
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Reason).HasMaxLength(200);
            b.HasIndex(x => new { x.PersonId, x.To });
        });

        modelBuilder.Entity<Assignment>(b =>
        {
            b.ToTable("assignments");
            b.Property(a => a.Id).ValueGeneratedNever();
            b.Property(a => a.Status).HasConversion<string>().HasMaxLength(10);
            b.Property(a => a.Scope).HasMaxLength(512);
            b.Property(a => a.DeclineReason).HasMaxLength(300);
            b.HasIndex(a => new { a.PlanId, a.PositionId, a.PersonId }).IsUnique();
            b.HasIndex(a => new { a.PersonId, a.Date });
            b.HasIndex(a => new { a.Date, a.Status });
            b.Property<uint>("xmin").IsRowVersion();
        });

        modelBuilder.Entity<ServiceType>(b =>
        {
            b.ToTable("service_types");
            b.Property(t => t.Id).ValueGeneratedNever();
            b.Property(t => t.Name).HasMaxLength(80);
            b.Property(t => t.Scope).HasMaxLength(512);
            JsonList(b.Property(t => t.Items));
            JsonList(b.Property(t => t.Needs));
        });

        modelBuilder.Entity<Plan>(b =>
        {
            b.ToTable("plans");
            b.Property(p => p.Id).ValueGeneratedNever();
            b.Property(p => p.Title).HasMaxLength(120);
            b.Property(p => p.SeriesTitle).HasMaxLength(120);
            b.Property(p => p.Notes).HasMaxLength(4000);
            b.Property(p => p.Scope).HasMaxLength(512);
            JsonList(b.Property(p => p.Items));
            JsonList(b.Property(p => p.Needs));
            b.Ignore(p => p.IsLive);
            b.Ignore(p => p.TotalSeconds);
            b.HasIndex(p => p.Date);
            b.HasIndex(p => p.Scope).HasOperators("text_pattern_ops");
            b.Property<uint>("xmin").IsRowVersion();
        });

        modelBuilder.Entity<Song>(b =>
        {
            b.ToTable("songs");
            b.Property(s => s.Id).ValueGeneratedNever();
            b.Property(s => s.Title).HasMaxLength(150);
            b.Property(s => s.Author).HasMaxLength(200);
            b.Property(s => s.CcliNumber).HasMaxLength(12);
            b.Property(s => s.Lyrics).HasMaxLength(Song.MaxLyrics);
            b.Property(s => s.ReferenceUrl).HasMaxLength(500);
            b.Property(s => s.Scope).HasMaxLength(512);
            JsonList(b.Property(s => s.Arrangements));
            b.HasIndex(s => s.Title);
            b.HasIndex(s => s.CcliNumber);
        });
    }

    protected override IEnumerable<IIntegrationEvent> ToIntegrationEvents(IDomainEvent domainEvent) => domainEvent switch
    {
        ServingRequested e => [new ServingRequestedIntegrationEvent(e.AssignmentId, e.PersonId, e.PlanTitle, e.Date, e.Team, e.Position)],
        ServingReminderDue e => [new ServingReminderIntegrationEvent(e.AssignmentId, e.PersonId, e.PlanTitle, e.Date, e.Team, e.Position)],
        ServingDeclined e => [new ServingDeclinedIntegrationEvent(e.AssignmentId, e.PlanId, e.PlanTitle, e.Date, e.Team, e.Position, e.Scope)],
        _ => [],
    };

    /// <summary>Small lists that belong to their row and are always read with it: stored as JSON.</summary>
    private static void JsonList<T>(PropertyBuilder<List<T>> property) =>
        property
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, Json),
                v => JsonSerializer.Deserialize<List<T>>(v, Json) ?? new List<T>(),
                new ValueComparer<List<T>>(
                    (a, b) => JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(b, Json),
                    v => JsonSerializer.Serialize(v, Json).GetHashCode(StringComparison.Ordinal),
                    v => JsonSerializer.Deserialize<List<T>>(JsonSerializer.Serialize(v, Json), Json)!));
}

/// <summary>Answer links last until the service has passed (up to 90 days), signed with the API's data protection keys.</summary>
internal sealed class DataProtectionAnswerLinks(IDataProtectionProvider provider) : IAnswerLinks
{
    private readonly ITimeLimitedDataProtector _protector = provider.CreateProtector("Shapers.Services.AnswerLink").ToTimeLimitedDataProtector();

    public string Token(Guid assignmentId) => _protector.Protect(assignmentId.ToString("N"), TimeSpan.FromDays(90));

    public Guid? Read(string token)
    {
        try
        {
            return Guid.TryParseExact(_protector.Unprotect(token), "N", out var id) ? id : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}

public static class ServicesInfrastructure
{
    public static IServiceCollection AddServicesInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<ServicesDbContext>(configuration, ServicesDbContext.SchemaName, typeof(ServingRequestedIntegrationEvent).Assembly);
        services.AddScoped<IServicesDb>(sp => sp.GetRequiredService<ServicesDbContext>());
        services.AddSingleton<IPermissionProvider, ServicesPermissionProvider>();
        services.Configure<ServicesOptions>(configuration.GetSection(ServicesOptions.SectionName));
        services.PostConfigure<ServicesOptions>(o => o.PublicApiUrl ??= configuration["Communications:PublicApiUrl"]);
        services.AddSingleton<IAnswerLinks, DataProtectionAnswerLinks>();

        services.AddScoped<PlanReader>();
        services.AddScoped<TeamService>();
        services.AddScoped<CategoryService>();
        services.AddScoped<PlanService>();
        services.AddScoped<ScheduleService>();
        services.AddScoped<LiveService>();
        services.AddScoped<SongService>();
        services.AddScoped<MyServingService>();
        services.AddScoped<AnswerByLinkService>();
        services.AddScoped<ServingReminderJob>();
        services.AddScoped<ServicesDemoSeeder>();
        services.AddScoped<IIntegrationEventHandler<ServingRequestedIntegrationEvent>, ServingEmails>();
        services.AddScoped<IIntegrationEventHandler<ServingReminderIntegrationEvent>, ServingEmails>();
        services.AddScoped<IIntegrationEventHandler<PeopleMergedIntegrationEvent>, ReplaceMergedServingPerson>();
        services.AddScoped<Shapers.Platform.Privacy.IPersonalDataSource, ServicesPersonalData>();
        services.AddScoped<Shapers.Platform.Calendar.ICalendarSource, ServicesCalendar>();

        // 07:30 in Johannesburg.
        services.AddSingleton(new RecurringJobDefinition("services-reminders", "30 5 * * *", (sp, ct) =>
            sp.GetRequiredService<ServingReminderJob>().RunAsync(ct)));
        return services;
    }

    public static async Task InitialiseServicesAsync(this IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ServicesDbContext>().Database.MigrateAsync(cancellationToken);

        // Every church starts with the typical team categories; it can change them afterwards.
        await scope.ServiceProvider.GetRequiredService<CategoryService>().EnsureTypicalAsync(cancellationToken);

        // Typical teams, songs and the coming Sundays, so a new development or demo database shows Services working.
        var environment = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
        if (environment.IsDevelopment() || environment.IsEnvironment("Demo"))
        {
            await scope.ServiceProvider.GetRequiredService<ServicesDemoSeeder>().SeedAsync(cancellationToken);
        }
    }
}
