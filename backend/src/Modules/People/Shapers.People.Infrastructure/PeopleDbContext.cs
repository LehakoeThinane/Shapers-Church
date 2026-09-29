using Microsoft.EntityFrameworkCore;
using Shapers.People.Application;
using Shapers.People.Contracts;
using Shapers.People.Domain;
using Shapers.Platform.Persistence;

namespace Shapers.People.Infrastructure;

public sealed class PeopleDbContext(DbContextOptions<PeopleDbContext> options) : ModuleDbContext(options), IPeopleDb
{
    public const string SchemaName = "people";

    public override string Schema => SchemaName;

    public DbSet<Person> Persons => Set<Person>();

    public DbSet<Household> Households => Set<Household>();

    public DbSet<MembershipStatus> MembershipStatuses => Set<MembershipStatus>();

    public DbSet<ConsentRecord> ConsentRecords => Set<ConsentRecord>();

    public DbSet<DuplicateCandidate> DuplicateCandidates => Set<DuplicateCandidate>();

    public DbSet<PersonMerge> PersonMerges => Set<PersonMerge>();

    public DbSet<ConnectCard> ConnectCards => Set<ConnectCard>();

    Task<int> IPeopleDb.SaveChangesAsync(CancellationToken cancellationToken) => SaveChangesAsync(cancellationToken);

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Person>(b =>
        {
            b.ToTable("persons");
            b.Property(p => p.Id).ValueGeneratedNever();
            b.Property(p => p.Scope).HasMaxLength(512);
            b.Property(p => p.FirstName).HasMaxLength(100);
            b.Property(p => p.LastName).HasMaxLength(100);
            b.Property(p => p.PreferredName).HasMaxLength(100);
            b.Property(p => p.Gender).HasConversion<string>().HasMaxLength(10);
            b.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(p => p.Source).HasConversion<string>().HasMaxLength(30);
            b.Ignore(p => p.DisplayName);

            // text_pattern_ops lets "scope LIKE 'shapers.campus_rivonia.%'" use the index.
            b.HasIndex(p => p.Scope).HasOperators("text_pattern_ops");
            b.HasIndex(p => new { p.LastName, p.FirstName });
            b.HasIndex(p => p.MergedIntoId);
            b.HasOne<MembershipStatus>().WithMany().HasForeignKey(p => p.MembershipStatusId).OnDelete(DeleteBehavior.Restrict);

            b.OwnsMany(p => p.Contacts, c =>
            {
                c.ToTable("person_contacts");
                c.WithOwner().HasForeignKey("PersonId");
                c.HasKey(x => x.Id);
                c.Property(x => x.Id).ValueGeneratedNever();
                c.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
                c.Property(x => x.Value).HasMaxLength(254);
                c.HasIndex(x => x.Value);
            });
            b.Navigation(p => p.Contacts).HasField("_contacts");

            b.OwnsMany(p => p.StatusHistory, h =>
            {
                h.ToTable("membership_status_changes");
                h.WithOwner().HasForeignKey("PersonId");
                h.HasKey(x => x.Id);
                h.Property(x => x.Id).ValueGeneratedNever();
            });
            b.Navigation(p => p.StatusHistory).HasField("_statusHistory");

            b.Property<uint>("xmin").IsRowVersion();
        });

        modelBuilder.Entity<Household>(b =>
        {
            b.ToTable("households");
            b.Property(h => h.Id).ValueGeneratedNever();
            b.Property(h => h.Name).HasMaxLength(200);
            b.Property(h => h.Scope).HasMaxLength(512);
            b.HasIndex(h => h.Scope).HasOperators("text_pattern_ops");
            b.OwnsMany(h => h.Members, m =>
            {
                m.ToTable("household_members");
                m.WithOwner().HasForeignKey("HouseholdId");
                m.HasKey("HouseholdId", nameof(HouseholdMember.PersonId));
                m.Property(x => x.Role).HasConversion<string>().HasMaxLength(10);
                m.HasIndex(x => x.PersonId);
            });
            b.Navigation(h => h.Members).HasField("_members");
            b.Property<uint>("xmin").IsRowVersion();
        });

        modelBuilder.Entity<MembershipStatus>(b =>
        {
            b.ToTable("membership_statuses");
            b.Property(s => s.Id).ValueGeneratedNever();
            b.Property(s => s.Name).HasMaxLength(100);
            b.Property(s => s.Stage).HasConversion<string>().HasMaxLength(20);
            b.HasIndex(s => s.IsDefault).IsUnique().HasFilter("is_default");
        });

        modelBuilder.Entity<ConsentRecord>(b =>
        {
            b.ToTable("consent_records");
            b.Property(c => c.Id).ValueGeneratedNever();
            b.Property(c => c.Purpose).HasMaxLength(100);
            b.Property(c => c.PolicyVersion).HasMaxLength(50);
            b.Property(c => c.LawfulBasis).HasConversion<string>().HasMaxLength(30);
            b.Property(c => c.Source).HasConversion<string>().HasMaxLength(30);
            b.HasIndex(c => new { c.PersonId, c.Purpose, c.RecordedAt });
        });

        modelBuilder.Entity<DuplicateCandidate>(b =>
        {
            b.ToTable("duplicate_candidates");
            b.Property(d => d.Id).ValueGeneratedNever();
            b.Property(d => d.Reasons).HasMaxLength(500);
            b.Property(d => d.Status).HasConversion<string>().HasMaxLength(20);
            b.HasIndex(d => new { d.PersonAId, d.PersonBId }).IsUnique();
            b.HasIndex(d => d.Status);
        });

        modelBuilder.Entity<ConnectCard>(b =>
        {
            b.ToTable("connect_cards");
            b.Property(c => c.Id).ValueGeneratedNever();
            b.Property(c => c.Scope).HasMaxLength(512);
            b.Property(c => c.Reasons).HasConversion(
                v => v.Select(r => r.ToString()).ToArray(),
                v => v.Select(Enum.Parse<ConnectReason>).ToList(),
                new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<ConnectReason>>(
                    (a, b) => a!.SequenceEqual(b!),
                    v => v.Aggregate(0, (h, r) => HashCode.Combine(h, r)),
                    v => v.ToList()));
            b.Property(c => c.Message).HasMaxLength(2000);
            b.Property(c => c.Source).HasMaxLength(30);
            b.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(c => c.HandlerNote).HasMaxLength(1000);
            b.HasIndex(c => c.Scope).HasOperators("text_pattern_ops");
            b.HasIndex(c => new { c.Status, c.SubmittedAt });
        });

        modelBuilder.Entity<PersonMerge>(b =>
        {
            b.ToTable("person_merges");
            b.Property(m => m.Id).ValueGeneratedNever();
            b.Property(m => m.Snapshot).HasColumnType("jsonb");
            b.HasIndex(m => m.MergedId);
            b.HasIndex(m => m.SurvivorId);
        });
    }

    protected override IEnumerable<IIntegrationEvent> ToIntegrationEvents(IDomainEvent domainEvent) => domainEvent switch
    {
        PersonCreated e => [new PersonCreatedIntegrationEvent(e.PersonId, e.Scope, e.Source.ToString())],
        PersonMerged e => [new PeopleMergedIntegrationEvent(e.SurvivorId, e.MergedId)],
        MembershipStatusChanged e => [new MembershipStatusChangedIntegrationEvent(e.PersonId, e.ToStage.ToString())],
        PersonMovedCampus e => [new PersonMovedCampusIntegrationEvent(e.PersonId, e.FromScope, e.ToScope)],
        ConnectCardSubmitted e => [new ConnectCardSubmittedIntegrationEvent(e.CardId, e.PersonId, e.Scope, e.Reasons.Select(r => r.ToString()).ToList())],
        _ => [],
    };
}
