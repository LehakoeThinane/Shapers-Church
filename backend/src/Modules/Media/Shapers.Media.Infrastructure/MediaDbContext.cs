using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;
using Shapers.Media.Application;
using Shapers.Media.Contracts;
using Shapers.Media.Domain;
using Shapers.Platform.Persistence;

namespace Shapers.Media.Infrastructure;

public sealed class MediaDbContext(DbContextOptions<MediaDbContext> options) : ModuleDbContext(options), IMediaDb
{
    public const string SchemaName = "media";

    public override string Schema => SchemaName;

    public DbSet<Sermon> Sermons => Set<Sermon>();

    public DbSet<Series> Series => Set<Series>();

    public DbSet<Speaker> Speakers => Set<Speaker>();

    public DbSet<MediaAsset> Assets => Set<MediaAsset>();

    public DbSet<PlaybackPosition> PlaybackPositions => Set<PlaybackPosition>();

    Task<int> IMediaDb.SaveChangesAsync(CancellationToken cancellationToken) => SaveChangesAsync(cancellationToken);

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Sermon>(b =>
        {
            b.ToTable("sermons");
            b.Property(s => s.Id).ValueGeneratedNever();
            b.Property(s => s.Title).HasMaxLength(200);
            b.Property(s => s.Slug).HasMaxLength(100);
            b.Property(s => s.Scope).HasMaxLength(512);
            b.Property(s => s.Summary).HasMaxLength(1000);
            b.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(s => s.Language).HasMaxLength(10);
            b.Property(s => s.ImportSource).HasMaxLength(100);
            b.Ignore(s => s.IsPublic);
            b.HasIndex(s => s.Slug).IsUnique();
            b.HasIndex(s => s.ImportSource).IsUnique().HasFilter("import_source IS NOT NULL");
            b.HasIndex(s => new { s.Status, s.PreachedOn });
            b.HasIndex(s => s.SeriesId);
            b.HasIndex(s => s.Scope).HasOperators("text_pattern_ops");
            b.HasIndex(s => s.Topics).HasMethod("gin");

            // Full-text search: Postgres keeps this column in step with search_text.
            b.Property<NpgsqlTsVector>("SearchVector")
                .HasComputedColumnSql("to_tsvector('english', coalesce(search_text, ''))", stored: true);
            b.HasIndex("SearchVector").HasMethod("gin");

            b.OwnsOne(s => s.Video, v =>
            {
                v.Property(x => x.Provider).HasConversion<string>().HasMaxLength(20);
                v.Property(x => x.ExternalId).HasMaxLength(64);
            });

            b.OwnsMany(s => s.Speakers, sp =>
            {
                sp.ToTable("sermon_speakers");
                sp.WithOwner().HasForeignKey("SermonId");
                sp.HasKey("SermonId", nameof(SermonSpeaker.SpeakerId));
                sp.HasIndex(x => x.SpeakerId);
            });
            b.Navigation(s => s.Speakers).HasField("_speakers");

            b.OwnsMany(s => s.Scripture, r =>
            {
                r.ToTable("sermon_scripture");
                r.WithOwner().HasForeignKey("SermonId");
                r.Property<int>("Id").UseIdentityAlwaysColumn();
                r.HasKey("Id");
                r.Ignore(x => x.Book);
                r.HasIndex(x => x.BookNumber);
            });
            b.Navigation(s => s.Scripture).HasField("_scripture");

            b.Property<uint>("xmin").IsRowVersion();
        });

        modelBuilder.Entity<Series>(b =>
        {
            b.ToTable("series");
            b.Property(s => s.Id).ValueGeneratedNever();
            b.Property(s => s.Title).HasMaxLength(150);
            b.Property(s => s.Slug).HasMaxLength(100);
            b.Property(s => s.Description).HasMaxLength(2000);
            b.Property(s => s.Scope).HasMaxLength(512);
            b.HasIndex(s => s.Slug).IsUnique();
        });

        modelBuilder.Entity<Speaker>(b =>
        {
            b.ToTable("speakers");
            b.Property(s => s.Id).ValueGeneratedNever();
            b.Property(s => s.Name).HasMaxLength(120);
            b.Property(s => s.Title).HasMaxLength(120);
            b.Property(s => s.Bio).HasMaxLength(2000);
            b.HasIndex(s => s.Name);
        });

        modelBuilder.Entity<MediaAsset>(b =>
        {
            b.ToTable("assets");
            b.Property(a => a.Id).ValueGeneratedNever();
            b.Property(a => a.Kind).HasConversion<string>().HasMaxLength(20);
            b.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(a => a.StorageKey).HasMaxLength(200);
            b.Property(a => a.ContentType).HasMaxLength(100);
            b.Property(a => a.OriginalFileName).HasMaxLength(200);
            b.HasIndex(a => a.StorageKey).IsUnique();
        });

        modelBuilder.Entity<PlaybackPosition>(b =>
        {
            b.ToTable("playback_positions");
            b.HasKey(p => new { p.PersonId, p.SermonId });
            b.HasIndex(p => p.UpdatedAt);
        });
    }

    protected override IEnumerable<IIntegrationEvent> ToIntegrationEvents(IDomainEvent domainEvent) => domainEvent switch
    {
        SermonPublished e => [new SermonPublishedIntegrationEvent(e.SermonId, e.Title, e.Slug, e.Scope)],
        _ => [],
    };
}
