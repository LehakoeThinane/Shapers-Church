using Microsoft.EntityFrameworkCore;
using Shapers.Documents.Application;
using Shapers.Documents.Domain;
using Shapers.Platform.Persistence;

namespace Shapers.Documents.Infrastructure;

public sealed class DocumentsDbContext(DbContextOptions<DocumentsDbContext> options) : ModuleDbContext(options), IDocumentsDb
{
    public const string SchemaName = "documents";

    public override string Schema => SchemaName;

    public DbSet<Library> Libraries => Set<Library>();

    public DbSet<Folder> Folders => Set<Folder>();

    public DbSet<Document> Documents => Set<Document>();

    Task<int> IDocumentsDb.SaveChangesAsync(CancellationToken cancellationToken) => SaveChangesAsync(cancellationToken);

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Library>(b =>
        {
            b.ToTable("libraries");
            b.Property(l => l.Id).ValueGeneratedNever();
            b.Property(l => l.Name).HasMaxLength(DocumentNames.MaxLength);
            b.Property(l => l.Scope).HasMaxLength(512);
            b.HasIndex(l => new { l.Scope, l.Name }).IsUnique();
            b.OwnsMany(l => l.Members, m => m.ToJson("members"));
            b.Navigation(l => l.Members).HasField("_members");
            b.Property<uint>("xmin").IsRowVersion();
        });

        modelBuilder.Entity<Folder>(b =>
        {
            b.ToTable("folders");
            b.Property(f => f.Id).ValueGeneratedNever();
            b.Property(f => f.Name).HasMaxLength(DocumentNames.MaxLength);
            b.HasIndex(f => new { f.LibraryId, f.ParentId });
        });

        modelBuilder.Entity<Document>(b =>
        {
            b.ToTable("documents");
            b.Property(d => d.Id).ValueGeneratedNever();
            b.Property(d => d.Name).HasMaxLength(DocumentNames.MaxLength);
            b.HasIndex(d => new { d.LibraryId, d.FolderId });
            b.HasIndex(d => d.UpdatedAt);
            b.Ignore(d => d.Current);
            b.OwnsMany(d => d.Versions, v =>
            {
                v.ToJson("versions");
                v.Property(x => x.StorageKey).HasMaxLength(300);
                v.Property(x => x.ContentType).HasMaxLength(200);
                v.Property(x => x.UploadedByName).HasMaxLength(200);
            });
            b.Navigation(d => d.Versions).HasField("_versions");
            b.Property<uint>("xmin").IsRowVersion();
        });
    }
}
