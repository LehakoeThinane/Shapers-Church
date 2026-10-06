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

    public DbSet<Livestream> Livestreams => Set<Livestream>();

    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();

    public DbSet<ChatSanction> ChatSanctions => Set<ChatSanction>();

    public DbSet<ChatBlockedTerm> ChatBlockedTerms => Set<ChatBlockedTerm>();

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
            b.Property(s => s.TranscriptSource).HasConversion<string>().HasMaxLength(10);
            b.Property(s => s.TranscriptStatus).HasConversion<string>().HasMaxLength(10);
            b.Property(s => s.TranscriptError).HasMaxLength(300);
            b.HasIndex(s => s.TranscriptStatus).HasFilter("transcript_status IN ('Queued', 'Working')");
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

        modelBuilder.Entity<Livestream>(b =>
        {
            b.ToTable("livestreams");
            b.Property(s => s.Id).ValueGeneratedNever();
            b.Property(s => s.Title).HasMaxLength(150);
            b.Property(s => s.Scope).HasMaxLength(512);
            b.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(s => s.GiveUrl).HasMaxLength(500);
            b.Ignore(s => s.CurrentCue);
            b.HasIndex(s => new { s.Status, s.ScheduledStart });
            b.OwnsOne(s => s.Video, v =>
            {
                v.Property(x => x.Provider).HasConversion<string>().HasMaxLength(20);
                v.Property(x => x.ExternalId).HasMaxLength(64);
            });
            b.OwnsMany(s => s.Cues, c =>
            {
                c.ToTable("livestream_cues");
                c.WithOwner().HasForeignKey("LivestreamId");
                c.HasKey(x => x.Id);
                c.Property(x => x.Id).ValueGeneratedNever();
                c.Property(x => x.Reference).HasMaxLength(100);
                c.Property(x => x.Text).HasMaxLength(4000);
            });
            b.Navigation(s => s.Cues).HasField("_cues");
            b.Property<uint>("xmin").IsRowVersion();
        });

        modelBuilder.Entity<ChatMessage>(b =>
        {
            b.ToTable("chat_messages");
            b.Property(m => m.Id).ValueGeneratedNever();
            b.Property(m => m.Scope).HasMaxLength(512);
            b.Property(m => m.AuthorName).HasMaxLength(80);
            b.Property(m => m.Text).HasMaxLength(ChatRules.MaxLength);
            b.Property(m => m.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(m => m.HoldReason).HasConversion<string>().HasMaxLength(20);
            b.Ignore(m => m.IsEvidence);
            b.HasIndex(m => new { m.LivestreamId, m.SentAt });
            b.HasIndex(m => new { m.PersonId, m.SentAt });
            b.HasIndex(m => m.SentAt);
            b.OwnsMany(m => m.Reports, r =>
            {
                r.ToTable("chat_reports");
                r.WithOwner().HasForeignKey("MessageId");
                // A generated key, so EF inserts new reports rather than treating them as existing rows.
                r.Property<long>("Id").UseIdentityAlwaysColumn();
                r.HasKey("Id");
                r.HasIndex("MessageId", nameof(ChatReport.ReporterId)).IsUnique();
                r.HasIndex(x => x.ReporterId);
            });
            b.Navigation(m => m.Reports).HasField("_reports");
            b.Property<uint>("xmin").IsRowVersion();
        });

        modelBuilder.Entity<ChatSanction>(b =>
        {
            b.ToTable("chat_sanctions");
            b.Property(s => s.Id).ValueGeneratedNever();
            b.Property(s => s.Kind).HasConversion<string>().HasMaxLength(20);
            b.Property(s => s.AuthorName).HasMaxLength(80);
            b.Property(s => s.Reason).HasMaxLength(300);
            b.Ignore(s => s.IsActive);
            b.HasIndex(s => new { s.PersonId, s.LiftedAt });
        });

        modelBuilder.Entity<ChatBlockedTerm>(b =>
        {
            b.ToTable("chat_blocked_terms");
            b.HasKey(t => t.Term);
            b.Property(t => t.Term).HasMaxLength(60);
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
        LivestreamStarted e => [new LivestreamStartedIntegrationEvent(e.LivestreamId, e.Title, e.Scope)],
        ChatMessageReported e => [new ChatMessageReportedIntegrationEvent(e.MessageId, e.LivestreamId, e.Scope)],
        _ => [],
    };
}
