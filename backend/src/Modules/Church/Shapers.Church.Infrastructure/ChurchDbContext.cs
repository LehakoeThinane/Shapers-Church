using Microsoft.EntityFrameworkCore;
using Shapers.Church.Application;
using Shapers.Church.Contracts;
using Shapers.Church.Domain;
using Shapers.Platform.Persistence;
using Shapers.SharedKernel;

namespace Shapers.Church.Infrastructure;

public sealed class ChurchDbContext(DbContextOptions<ChurchDbContext> options) : ModuleDbContext(options), IChurchDb
{
    public const string SchemaName = "church";

    public override string Schema => SchemaName;

    public DbSet<Organisation> Organisations => Set<Organisation>();

    public DbSet<Campus> Campuses => Set<Campus>();

    public DbSet<Ministry> Ministries => Set<Ministry>();

    Task<int> IChurchDb.SaveChangesAsync(CancellationToken cancellationToken) => SaveChangesAsync(cancellationToken);

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Organisation>(b =>
        {
            b.ToTable("organisations");
            b.Property(o => o.Id).ValueGeneratedNever();
            b.Property(o => o.Name).HasMaxLength(200);
            b.Property(o => o.Slug).HasMaxLength(100);
            b.Property(o => o.LegalName).HasMaxLength(200);
            b.Property(o => o.PboNumber).HasMaxLength(50);
            b.Property(o => o.TimeZone).HasMaxLength(64);
            b.Property(o => o.Currency).HasMaxLength(3);
            b.Property(o => o.ContactEmail).HasMaxLength(254);
            b.Property(o => o.Website).HasMaxLength(300);
            b.Property(o => o.Scope).HasMaxLength(512);
            b.HasIndex(o => o.Slug).IsUnique();
            b.Property<uint>("xmin").IsRowVersion();
        });

        modelBuilder.Entity<Campus>(b =>
        {
            b.ToTable("campuses");
            b.Property(c => c.Id).ValueGeneratedNever();
            b.Property(c => c.Name).HasMaxLength(200);
            b.Property(c => c.Slug).HasMaxLength(100);
            b.Property(c => c.Scope).HasMaxLength(512);
            b.Property(c => c.TimeZone).HasMaxLength(64);
            b.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
            b.HasIndex(c => c.Slug).IsUnique();
            b.HasIndex(c => c.Scope).IsUnique();
            b.HasOne<Organisation>().WithMany().HasForeignKey(c => c.OrganisationId);
            b.OwnsOne(c => c.Address, a =>
            {
                a.Property(x => x.Line1).HasMaxLength(200);
                a.Property(x => x.Line2).HasMaxLength(200);
                a.Property(x => x.Suburb).HasMaxLength(100);
                a.Property(x => x.City).HasMaxLength(100);
                a.Property(x => x.Province).HasMaxLength(100);
                a.Property(x => x.PostalCode).HasMaxLength(20);
                a.Property(x => x.CountryCode).HasMaxLength(2);
            });
            b.Property<uint>("xmin").IsRowVersion();
        });

        modelBuilder.Entity<Ministry>(b =>
        {
            b.ToTable("ministries");
            b.Property(m => m.Id).ValueGeneratedNever();
            b.Property(m => m.Name).HasMaxLength(200);
            b.Property(m => m.Slug).HasMaxLength(100);
            b.Property(m => m.Scope).HasMaxLength(512);
            b.HasIndex(m => m.Scope).IsUnique();
            b.HasOne<Organisation>().WithMany().HasForeignKey(m => m.OrganisationId);
            b.HasOne<Campus>().WithMany().HasForeignKey(m => m.CampusId);
            b.Property<uint>("xmin").IsRowVersion();
        });
    }

    protected override IEnumerable<IIntegrationEvent> ToIntegrationEvents(IDomainEvent domainEvent) => domainEvent switch
    {
        CampusCreated e => [new CampusCreatedIntegrationEvent(e.CampusId, e.Name, e.Scope)],
        MinistryCreated e => [new MinistryCreatedIntegrationEvent(e.MinistryId, e.CampusId, e.Name, e.Scope)],
        _ => [],
    };
}
