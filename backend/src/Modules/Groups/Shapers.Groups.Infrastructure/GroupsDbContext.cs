using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shapers.Groups.Application;
using Shapers.Groups.Contracts;
using Shapers.Groups.Domain;
using Shapers.Platform.Persistence;

namespace Shapers.Groups.Infrastructure;

public sealed class GroupsDbContext(DbContextOptions<GroupsDbContext> options) : ModuleDbContext(options), IGroupsDb
{
    public const string SchemaName = "groups";

    public override string Schema => SchemaName;

    public DbSet<Cell> Cells => Set<Cell>();

    public DbSet<CellMember> Members => Set<CellMember>();

    public DbSet<CellReport> Reports => Set<CellReport>();

    public DbSet<CellMaterial> Materials => Set<CellMaterial>();

    Task<int> IGroupsDb.SaveChangesAsync(CancellationToken cancellationToken) => SaveChangesAsync(cancellationToken);

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Cell>(b =>
        {
            b.ToTable("cells");
            b.Property(c => c.Id).ValueGeneratedNever();
            b.Property(c => c.Name).HasMaxLength(Cell.MaxNameLength);
            b.Property(c => c.Scope).HasMaxLength(512);
            b.Property(c => c.Area).HasMaxLength(100);
            b.Property(c => c.Address).HasMaxLength(300);
            b.Property(c => c.MeetingDay).HasConversion<string>().HasMaxLength(10);
            b.Property(c => c.Status).HasConversion<string>().HasMaxLength(10);
            b.HasMany(c => c.Members).WithOne().HasForeignKey(m => m.CellId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(c => c.Members).UsePropertyAccessMode(PropertyAccessMode.Field);
            // A filtered view of Members, not a second relationship.
            b.Ignore(c => c.ActiveMembers);
            b.HasIndex(c => c.Scope).HasOperators("text_pattern_ops");
            b.Property<uint>("xmin").IsRowVersion();
        });

        modelBuilder.Entity<CellMember>(b =>
        {
            b.ToTable("members");
            b.Property(m => m.Id).ValueGeneratedNever();
            b.Property(m => m.Role).HasConversion<string>().HasMaxLength(10);
            b.HasIndex(m => m.PersonId);
            // Someone belongs to a cell at most once at a time.
            b.HasIndex(m => new { m.CellId, m.PersonId }).IsUnique().HasFilter("left_at IS NULL");
        });

        modelBuilder.Entity<CellReport>(b =>
        {
            b.ToTable("reports");
            b.Property(r => r.Id).ValueGeneratedNever();
            b.Property(r => r.Scope).HasMaxLength(512);
            b.Property(r => r.Status).HasConversion<string>().HasMaxLength(10);
            b.Property(r => r.Multiplication).HasConversion<string>().HasMaxLength(10);
            b.Property(r => r.Topic).HasMaxLength(200);
            b.Property(r => r.Notes).HasMaxLength(CellReport.MaxTextLength);
            b.Property(r => r.Highlights).HasMaxLength(CellReport.MaxTextLength);
            b.Property(r => r.PrayerNeeds).HasMaxLength(CellReport.MaxTextLength);
            JsonList(b.Property(r => r.Visitors));
            JsonList(b.Property(r => r.FollowUps));
            JsonList(b.Property(r => r.Growth));
            b.HasIndex(r => new { r.CellId, r.MeetingDate }).IsUnique();
            b.HasIndex(r => r.Scope).HasOperators("text_pattern_ops");
            b.HasIndex(r => new { r.RedactedAt, r.MeetingDate });
            b.Property<uint>("xmin").IsRowVersion();
        });

        modelBuilder.Entity<CellMaterial>(b =>
        {
            b.ToTable("materials");
            b.Property(m => m.Id).ValueGeneratedNever();
            b.Property(m => m.Scope).HasMaxLength(512);
            b.Property(m => m.Title).HasMaxLength(160);
            b.Property(m => m.Body).HasMaxLength(CellMaterial.MaxBodyLength);
            b.Property(m => m.Link).HasMaxLength(500);
            b.HasIndex(m => new { m.CellId, m.UpdatedAt });
            b.Ignore(m => m.IsChurchLesson);
            b.HasIndex(m => m.Scope).HasOperators("text_pattern_ops");
        });
    }

    protected override IEnumerable<IIntegrationEvent> ToIntegrationEvents(IDomainEvent domainEvent) => domainEvent switch
    {
        CellReportSubmitted e => Submitted(e),
        _ => [],
    };

    private static IEnumerable<IIntegrationEvent> Submitted(CellReportSubmitted e)
    {
        if (e.VisitorsToContact.Count > 0)
        {
            yield return new CellVisitorsRecordedIntegrationEvent(
                e.ReportId, e.CellId, e.CellName, e.MeetingDate, e.VisitorsToContact.Select(v => new CellVisitor(v.FirstName, v.LastName, v.Mobile, v.Email)).ToList());
        }

        if (e.HasUrgentFollowUp)
        {
            yield return new CellFollowUpFlaggedIntegrationEvent(e.ReportId, e.CellId, e.CellName, e.Scope);
        }
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Small lists that belong to the report and are always read with it: stored as JSON.</summary>
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
