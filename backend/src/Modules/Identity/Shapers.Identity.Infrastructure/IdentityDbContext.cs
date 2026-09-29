using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Shapers.Identity.Application;
using Shapers.Identity.Domain;
using Shapers.Platform.Messaging;
using Shapers.Platform.Persistence;

namespace Shapers.Identity.Infrastructure;

/// <summary>
/// A login. Always linked to exactly one church record (Person). Members sign in by phone; staff by email,
/// password and a second factor.
/// </summary>
public sealed class User : IdentityUser<Guid>
{
    public Guid PersonId { get; set; }

    public string Palette { get; set; } = Palettes.Auto;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastSignInAt { get; set; }
}

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options)
    : IdentityUserContext<User, Guid>(options), IIdentityDb
{
    public const string SchemaName = "identity";

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<Grant> Grants => Set<Grant>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<OtpChallenge> OtpChallenges => Set<OtpChallenge>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public void Publish(IIntegrationEvent integrationEvent) => OutboxMessages.Add(OutboxMessage.From(integrationEvent));

    Task<int> IIdentityDb.SaveChangesAsync(CancellationToken cancellationToken) => SaveChangesAsync(cancellationToken);

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        // Identity aggregates don't publish domain events yet; kept so any future ones reach the outbox.
        DomainEventOutbox.Collect(this, _ => []);
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema(SchemaName);
        builder.ApplyOutbox();

        builder.Entity<User>(b =>
        {
            b.ToTable("users");
            b.Property(u => u.Palette).HasMaxLength(20);
            b.Property(u => u.PhoneNumber).HasMaxLength(20);
            b.HasIndex(u => u.PersonId).IsUnique();

            // Email is optional for members but must be unique when present; phone likewise.
            b.HasIndex(u => u.NormalizedEmail).IsUnique().HasFilter("normalized_email IS NOT NULL").HasDatabaseName("ux_users_normalized_email");
            b.HasIndex(u => u.PhoneNumber).IsUnique().HasFilter("phone_number IS NOT NULL");
        });
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");

        builder.Entity<Role>(b =>
        {
            b.ToTable("roles");
            b.Property(r => r.Id).ValueGeneratedNever();
            b.Property(r => r.Name).HasMaxLength(100);
            b.Property(r => r.Description).HasMaxLength(500);
            b.HasIndex(r => r.Name).IsUnique();
            b.Ignore(r => r.PermissionKeys);
            b.OwnsMany(r => r.Permissions, p =>
            {
                p.ToTable("role_permissions");
                p.WithOwner().HasForeignKey("RoleId");
                p.HasKey("RoleId", nameof(RolePermission.PermissionKey));
                p.Property(x => x.PermissionKey).HasMaxLength(100);
            });
            b.Navigation(r => r.Permissions).HasField("_permissions");
        });

        builder.Entity<Grant>(b =>
        {
            b.ToTable("grants");
            b.Property(g => g.Id).ValueGeneratedNever();
            b.Property(g => g.Scope).HasMaxLength(512);
            b.Property(g => g.Reason).HasMaxLength(500);
            b.HasIndex(g => g.UserId);
            b.HasOne<User>().WithMany().HasForeignKey(g => g.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<Role>().WithMany().HasForeignKey(g => g.RoleId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<RefreshToken>(b =>
        {
            b.ToTable("refresh_tokens");
            b.Property(t => t.Id).ValueGeneratedNever();
            b.Property(t => t.TokenHash).HasMaxLength(64);
            b.Property(t => t.RevocationReason).HasMaxLength(50);
            b.Property(t => t.AuthenticationMethods).HasMaxLength(50);
            b.Property(t => t.Device).HasMaxLength(200);
            b.HasIndex(t => t.TokenHash).IsUnique();
            b.HasIndex(t => t.FamilyId);
            b.HasOne<User>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<OtpChallenge>(b =>
        {
            b.ToTable("otp_challenges");
            b.Property(c => c.Id).ValueGeneratedNever();
            b.Property(c => c.Phone).HasMaxLength(20);
            b.Property(c => c.RequestedFromIp).HasMaxLength(64);
            b.HasIndex(c => new { c.Phone, c.CreatedAt });
        });
    }
}
