using Microsoft.EntityFrameworkCore;
using Shapers.Kids.Application;
using Shapers.Kids.Domain;
using Shapers.Platform.Persistence;

namespace Shapers.Kids.Infrastructure;

public sealed class KidsDbContext(DbContextOptions<KidsDbContext> options) : ModuleDbContext(options), IKidsDb
{
    public const string SchemaName = "kids";

    public override string Schema => SchemaName;

    public DbSet<KidsClass> Classes => Set<KidsClass>();

    public DbSet<CareNote> CareNotes => Set<CareNote>();

    public DbSet<CheckIn> CheckIns => Set<CheckIn>();

    Task<int> IKidsDb.SaveChangesAsync(CancellationToken cancellationToken) => SaveChangesAsync(cancellationToken);

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<KidsClass>(b =>
        {
            b.ToTable("classes");
            b.Property(c => c.Id).ValueGeneratedNever();
            b.Property(c => c.Name).HasMaxLength(80);
            b.Property(c => c.Scope).HasMaxLength(512);
        });

        modelBuilder.Entity<CareNote>(b =>
        {
            b.ToTable("care_notes");
            b.HasKey(n => n.ChildId);
            b.Property(n => n.Allergies).HasMaxLength(CareNote.MaxLength);
            b.Property(n => n.Medical).HasMaxLength(CareNote.MaxLength);
            b.Property(n => n.Other).HasMaxLength(CareNote.MaxLength);
            b.Ignore(n => n.IsEmpty);
        });

        modelBuilder.Entity<CheckIn>(b =>
        {
            b.ToTable("check_ins");
            b.Property(c => c.Id).ValueGeneratedNever();
            b.Property(c => c.ClassName).HasMaxLength(80);
            b.Property(c => c.Scope).HasMaxLength(512);
            b.Property(c => c.PickupCode).HasMaxLength(CheckIn.CodeLength);
            b.Property(c => c.Method).HasConversion<string>().HasMaxLength(10);
            b.Ignore(c => c.IsCollected);
            b.HasIndex(c => new { c.Date, c.PickupCode });
            b.HasIndex(c => new { c.ChildId, c.Date });
            b.HasIndex(c => c.GuardianId);
            b.HasIndex(c => c.Scope).HasOperators("text_pattern_ops");
            b.Property<uint>("xmin").IsRowVersion();
        });
    }
}
