using Microsoft.EntityFrameworkCore;
using Shapers.Giving.Application;
using Shapers.Giving.Domain;
using Shapers.Platform.Persistence;

namespace Shapers.Giving.Infrastructure;

public sealed class GivingDbContext(DbContextOptions<GivingDbContext> options) : ModuleDbContext(options), IGivingDb
{
    public const string SchemaName = "giving";

    public override string Schema => SchemaName;

    public DbSet<Fund> Funds => Set<Fund>();

    public DbSet<Gift> Gifts => Set<Gift>();

    Task<int> IGivingDb.SaveChangesAsync(CancellationToken cancellationToken) => SaveChangesAsync(cancellationToken);

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Fund>(b =>
        {
            b.ToTable("funds");
            b.Property(f => f.Id).ValueGeneratedNever();
            b.Property(f => f.Name).HasMaxLength(60);
            b.Property(f => f.Description).HasMaxLength(200);
        });

        modelBuilder.Entity<Gift>(b =>
        {
            b.ToTable("gifts");
            b.Property(g => g.Id).ValueGeneratedNever();
            b.Property(g => g.FundName).HasMaxLength(60);
            b.Property(g => g.Currency).HasMaxLength(3);
            b.Property(g => g.Method).HasConversion<string>().HasMaxLength(10);
            b.Property(g => g.Status).HasConversion<string>().HasMaxLength(10);
            b.Property(g => g.GiverName).HasMaxLength(120);
            b.Property(g => g.GiverEmail).HasMaxLength(254);
            b.Property(g => g.Note).HasMaxLength(300);
            b.Property(g => g.ProviderCheckoutId).HasMaxLength(100);
            b.Property(g => g.ProviderPaymentId).HasMaxLength(100);
            b.HasIndex(g => g.ProviderCheckoutId).IsUnique().HasFilter("provider_checkout_id IS NOT NULL");
            b.HasIndex(g => new { g.Status, g.GivenOn });
            b.HasIndex(g => g.PersonId);
            b.Property<uint>("xmin").IsRowVersion();
        });
    }
}
