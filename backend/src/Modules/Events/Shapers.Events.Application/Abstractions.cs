using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Shapers.Events.Contracts;
using Shapers.Events.Domain;
using Shapers.Platform.Authorization;

namespace Shapers.Events.Application;

public interface IEventsDb
{
    DbSet<Event> Events { get; }

    DbSet<Registration> Registrations { get; }

    DbSet<EmailVerification> EmailVerifications { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Locks the event row until the transaction ends, so two people can't take the last seat at once and
    /// a cancellation can't promote the same person twice.
    /// </summary>
    Task LockEventAsync(Guid eventId, CancellationToken cancellationToken);
}

public sealed class EventsOptions
{
    public const string SectionName = "Events";

    /// <summary>The public website, used for links in emails, e.g. https://shaperschurch.com.</summary>
    public string PublicSiteUrl { get; set; } = "https://shaperschurch.com";
}

public sealed class EventsPermissionProvider : IPermissionProvider
{
    public IEnumerable<PermissionDefinition> GetPermissions() =>
    [
        new(EventsPermissions.Edit, "events", "Create and edit events"),
        new(EventsPermissions.Publish, "events", "Publish and cancel events"),
        new(EventsPermissions.RegistrationsView, "events", "See who registered for events", IsSensitive: true),
        new(EventsPermissions.RegistrationsManage, "events", "Register or cancel on someone's behalf", IsSensitive: true),
        new(EventsPermissions.CheckIn, "events", "Check people in at the door"),
    ];
}
