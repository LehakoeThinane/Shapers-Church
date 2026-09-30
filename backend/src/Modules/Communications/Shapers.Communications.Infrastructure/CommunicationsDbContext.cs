using Microsoft.EntityFrameworkCore;
using Shapers.Communications.Application;
using Shapers.Communications.Domain;
using Shapers.Platform.Persistence;

namespace Shapers.Communications.Infrastructure;

public sealed class CommunicationsDbContext(DbContextOptions<CommunicationsDbContext> options) : ModuleDbContext(options), ICommunicationsDb
{
    public const string SchemaName = "communications";

    public override string Schema => SchemaName;

    public DbSet<Device> Devices => Set<Device>();

    public DbSet<TopicPreference> Preferences => Set<TopicPreference>();

    public DbSet<Notification> Notifications => Set<Notification>();

    Task<int> ICommunicationsDb.SaveChangesAsync(CancellationToken cancellationToken) => SaveChangesAsync(cancellationToken);

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Device>(b =>
        {
            b.ToTable("devices");
            b.Property(d => d.Id).ValueGeneratedNever();
            b.Property(d => d.Token).HasMaxLength(200);
            b.Property(d => d.Platform).HasMaxLength(20);
            b.Property(d => d.Name).HasMaxLength(100);
            b.Property(d => d.DisabledReason).HasMaxLength(200);
            b.Ignore(d => d.IsActive);
            b.HasIndex(d => d.Token).IsUnique();
            b.HasIndex(d => d.PersonId);
        });

        modelBuilder.Entity<TopicPreference>(b =>
        {
            b.ToTable("preferences");
            b.HasKey(p => new { p.PersonId, p.Topic, p.Channel });
            b.Property(p => p.Topic).HasConversion<string>().HasMaxLength(20);
            b.Property(p => p.Channel).HasConversion<string>().HasMaxLength(20);
        });

        modelBuilder.Entity<Notification>(b =>
        {
            b.ToTable("notifications");
            b.Property(n => n.Id).ValueGeneratedNever();
            b.Property(n => n.Topic).HasConversion<string>().HasMaxLength(20);
            b.Property(n => n.Title).HasMaxLength(Notification.MaxTitle);
            b.Property(n => n.Body).HasMaxLength(Notification.MaxBody);
            b.Property(n => n.Link).HasMaxLength(300);
            b.Property(n => n.SourceKey).HasMaxLength(200);
            b.HasIndex(n => new { n.PersonId, n.SourceKey }).IsUnique();
            b.HasIndex(n => new { n.PersonId, n.CreatedAt });
            b.OwnsMany(n => n.Deliveries, d =>
            {
                d.ToTable("deliveries");
                d.WithOwner().HasForeignKey("NotificationId");
                d.HasKey(x => x.Id);
                d.Property(x => x.Id).ValueGeneratedNever();
                d.Property(x => x.Channel).HasConversion<string>().HasMaxLength(20);
                d.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
                d.Property(x => x.ProviderReference).HasMaxLength(200);
                d.Property(x => x.Error).HasMaxLength(500);
                d.HasIndex(x => new { x.Status, x.NotBefore });
            });
            b.Navigation(n => n.Deliveries).HasField("_deliveries");
            b.Property<uint>("xmin").IsRowVersion();
        });
    }
}
