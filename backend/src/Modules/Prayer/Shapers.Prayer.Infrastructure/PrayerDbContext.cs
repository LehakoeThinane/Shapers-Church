using Microsoft.EntityFrameworkCore;
using Shapers.Platform.Persistence;
using Shapers.Prayer.Application;
using Shapers.Prayer.Contracts;
using Shapers.Prayer.Domain;

namespace Shapers.Prayer.Infrastructure;

public sealed class PrayerDbContext(DbContextOptions<PrayerDbContext> options) : ModuleDbContext(options), IPrayerDb
{
    public const string SchemaName = "prayer";

    public override string Schema => SchemaName;

    public DbSet<PrayerRequest> Requests => Set<PrayerRequest>();

    public DbSet<PrayerResponse> Responses => Set<PrayerResponse>();

    Task<int> IPrayerDb.SaveChangesAsync(CancellationToken cancellationToken) => SaveChangesAsync(cancellationToken);

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PrayerRequest>(b =>
        {
            b.ToTable("requests");
            b.Property(r => r.Id).ValueGeneratedNever();
            b.Property(r => r.Scope).HasMaxLength(512);
            b.Property(r => r.Text).HasMaxLength(PrayerRequest.MaxLength);
            b.Property(r => r.WallText).HasMaxLength(PrayerRequest.MaxLength);
            b.Property(r => r.ReviewNote).HasMaxLength(500);
            b.Property(r => r.AnswerNote).HasMaxLength(PrayerRequest.MaxLength);
            b.Property(r => r.Visibility).HasConversion<string>().HasMaxLength(20);
            b.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(r => r.Source).HasConversion<string>().HasMaxLength(20);
            b.HasIndex(r => new { r.Status, r.ReviewedAt });
            b.HasIndex(r => new { r.PersonId, r.CreatedAt });
            b.HasIndex(r => r.Scope).HasOperators("text_pattern_ops");
            b.Property<uint>("xmin").IsRowVersion();
        });

        modelBuilder.Entity<PrayerResponse>(b =>
        {
            b.ToTable("responses");
            b.HasKey(x => new { x.RequestId, x.PersonId });
            b.HasOne<PrayerRequest>().WithMany().HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    protected override IEnumerable<IIntegrationEvent> ToIntegrationEvents(IDomainEvent domainEvent) => domainEvent switch
    {
        PrayerRequestSubmitted e => [new PrayerRequestSubmittedIntegrationEvent(e.RequestId, e.PersonId, e.Scope, e.Visibility == PrayerVisibility.Wall)],
        PrayerRequestApproved e => [new PrayerRequestApprovedIntegrationEvent(e.RequestId, e.PersonId)],
        _ => [],
    };
}
