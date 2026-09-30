using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using Shapers.Events.Application;
using Shapers.Events.Contracts;
using Shapers.Events.Domain;
using Shapers.Platform.Persistence;

namespace Shapers.Events.Infrastructure;

public sealed class EventsDbContext(DbContextOptions<EventsDbContext> options) : ModuleDbContext(options), IEventsDb
{
    public const string SchemaName = "events";

    public override string Schema => SchemaName;

    public DbSet<Event> Events => Set<Event>();

    public DbSet<Registration> Registrations => Set<Registration>();

    public DbSet<EmailVerification> EmailVerifications => Set<EmailVerification>();

    Task<int> IEventsDb.SaveChangesAsync(CancellationToken cancellationToken) => SaveChangesAsync(cancellationToken);

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken) => Database.BeginTransactionAsync(cancellationToken);

    public Task LockEventAsync(Guid eventId, CancellationToken cancellationToken) =>
        Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM events.events WHERE id = {eventId} FOR UPDATE", cancellationToken);

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Event>(b =>
        {
            b.ToTable("events");
            b.Property(e => e.Id).ValueGeneratedNever();
            b.Property(e => e.Title).HasMaxLength(150);
            b.Property(e => e.Slug).HasMaxLength(100);
            b.Property(e => e.Summary).HasMaxLength(300);
            b.Property(e => e.Scope).HasMaxLength(512);
            b.Property(e => e.ImageUrl).HasMaxLength(500);
            b.Property(e => e.Visibility).HasConversion<string>().HasMaxLength(20);
            b.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
            b.HasIndex(e => e.Slug).IsUnique();
            b.HasIndex(e => new { e.Status, e.StartsAt });
            b.HasIndex(e => e.Scope).HasOperators("text_pattern_ops");
            b.OwnsOne(e => e.Location, l =>
            {
                l.Property(x => x.Name).HasMaxLength(150);
                l.Property(x => x.Address).HasMaxLength(300);
            });
            b.OwnsMany(e => e.Questions, q =>
            {
                q.ToTable("event_questions");
                q.WithOwner().HasForeignKey("EventId");
                q.HasKey(x => x.Id);
                q.Property(x => x.Id).ValueGeneratedNever();
                q.Property(x => x.Label).HasMaxLength(200);
            });
            b.Navigation(e => e.Questions).HasField("_questions");
            b.Property<uint>("xmin").IsRowVersion();
        });

        modelBuilder.Entity<Registration>(b =>
        {
            b.ToTable("registrations");
            b.Property(r => r.Id).ValueGeneratedNever();
            b.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(r => r.Source).HasConversion<string>().HasMaxLength(20);
            b.Property(r => r.GuestKeyHash).HasMaxLength(64);
            b.Property(r => r.Answers)
                .HasColumnType("jsonb")
                .HasConversion(
                    v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
                    v => JsonSerializer.Deserialize<Dictionary<Guid, string>>(v, JsonSerializerOptions.Default) ?? new Dictionary<Guid, string>(),
                    new ValueComparer<Dictionary<Guid, string>>(
                        (a, c) => a!.Count == c!.Count && !a.Except(c).Any(),
                        v => v.Aggregate(0, (h, kv) => HashCode.Combine(h, kv.Key, kv.Value)),
                        v => v.ToDictionary()));
            b.Ignore(r => r.Seats);
            b.HasIndex(r => new { r.EventId, r.Status, r.CreatedAt });
            b.HasIndex(r => r.RegistrantPersonId);
            b.HasOne<Event>().WithMany().HasForeignKey(r => r.EventId).OnDelete(DeleteBehavior.Restrict);
            b.OwnsMany(r => r.Attendees, a =>
            {
                a.ToTable("attendees");
                a.WithOwner().HasForeignKey("RegistrationId");
                a.HasKey(x => x.Id);
                a.Property(x => x.Id).ValueGeneratedNever();
                a.Property(x => x.Name).HasMaxLength(120);
                a.Property(x => x.TicketCode).HasMaxLength(TicketCode.Length);
                a.HasIndex(x => x.TicketCode).IsUnique();
                a.HasIndex(x => x.PersonId);
            });
            b.Navigation(r => r.Attendees).HasField("_attendees");
            b.Property<uint>("xmin").IsRowVersion();
        });

        modelBuilder.Entity<EmailVerification>(b =>
        {
            b.ToTable("email_verifications");
            b.Property(v => v.Id).ValueGeneratedNever();
            b.Property(v => v.Email).HasMaxLength(254);
            b.Property(v => v.CodeHash).HasMaxLength(64);
            b.HasIndex(v => new { v.Email, v.CreatedAt });
        });
    }

    protected override IEnumerable<IIntegrationEvent> ToIntegrationEvents(IDomainEvent domainEvent) => domainEvent switch
    {
        RegistrationCreated e => [new EventRegisteredIntegrationEvent(e.RegistrationId, e.EventId, e.PersonId, e.Status.ToString())],
        WaitlistPromoted e => [new WaitlistPromotedIntegrationEvent(e.RegistrationId, e.EventId, e.PersonId)],
        EventCancelled e => [new EventCancelledIntegrationEvent(e.EventId, e.Title)],
        _ => [],
    };
}
