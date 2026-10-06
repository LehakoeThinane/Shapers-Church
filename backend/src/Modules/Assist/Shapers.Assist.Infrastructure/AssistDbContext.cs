using Microsoft.EntityFrameworkCore;
using Shapers.Assist.Application;
using Shapers.Assist.Domain;
using Shapers.Platform.Persistence;

namespace Shapers.Assist.Infrastructure;

public sealed class AssistDbContext(DbContextOptions<AssistDbContext> options) : ModuleDbContext(options), IAssistDb
{
    public const string SchemaName = "assist";

    public override string Schema => SchemaName;

    public DbSet<AiDraft> Drafts => Set<AiDraft>();

    public DbSet<AiUsage> Usage => Set<AiUsage>();

    Task<int> IAssistDb.SaveChangesAsync(CancellationToken cancellationToken) => SaveChangesAsync(cancellationToken);

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AiDraft>(b =>
        {
            b.ToTable("drafts");
            b.Property(d => d.Id).ValueGeneratedNever();
            b.Property(d => d.Kind).HasConversion<string>().HasMaxLength(20);
            b.Property(d => d.Status).HasConversion<string>().HasMaxLength(10);
            b.Property(d => d.SourceType).HasMaxLength(20);
            b.Property(d => d.Scope).HasMaxLength(512);
            b.Property(d => d.Model).HasMaxLength(100);
            b.Property(d => d.PromptVersion).HasMaxLength(60);
            b.Property(d => d.Output).HasColumnType("jsonb");
            b.HasIndex(d => new { d.SourceType, d.SourceId });
            b.HasIndex(d => d.RequestedAt);
            b.HasIndex(d => d.Scope).HasOperators("text_pattern_ops");
            b.Property<uint>("xmin").IsRowVersion();
        });

        modelBuilder.Entity<AiUsage>(b =>
        {
            b.ToTable("usage");
            b.Property(u => u.Id).ValueGeneratedNever();
            b.Property(u => u.Operation).HasConversion<string>().HasMaxLength(20);
            b.Property(u => u.Purpose).HasMaxLength(60);
            b.Property(u => u.Model).HasMaxLength(100);
            b.Property(u => u.CostZar).HasPrecision(12, 4);
            b.HasIndex(u => u.OccurredAt);
        });
    }
}
