using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shapers.People.Contracts;
using Shapers.Platform.Auditing;
using Shapers.Platform.Authorization;
using Shapers.Privacy.Domain;

namespace Shapers.Privacy.Application;

public sealed class PrivacyNoticeOptions
{
    public const string SectionName = "Privacy:Notice";

    /// <summary>Change this whenever the notice text changes: members are asked to review the new version.</summary>
    public string Version { get; set; } = "2026-09";

    public DateOnly EffectiveFrom { get; set; } = new(2026, 9, 30);
}

public sealed record PrivacyNoticeDto(string Version, DateOnly EffectiveFrom, string Markdown);

public sealed record PrivacyStatusDto(string CurrentVersion, string? AcceptedVersion, bool NeedsReview);

/// <summary>The privacy notice the app and website show, and whether a member has accepted the current version.</summary>
public sealed class PrivacyNoticeService(IOptions<PrivacyNoticeOptions> options, IPeopleDirectory people, ICurrentUser currentUser)
{
    private static readonly Lazy<string> Text = new(() =>
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Shapers.Privacy.Application.privacy-notice.md")
            ?? throw new InvalidOperationException("The privacy notice resource is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    public PrivacyNoticeDto Current() => new(options.Value.Version, options.Value.EffectiveFrom, Text.Value);

    public async Task<Result<PrivacyStatusDto>> StatusAsync(CancellationToken cancellationToken)
    {
        if (currentUser.PersonId is not { } me)
        {
            return Error.Unauthorized("privacy.sign_in", "Sign in first.");
        }

        var accepted = await people.ChurchRecordNoticeVersionAsync(me, cancellationToken);
        return new PrivacyStatusDto(options.Value.Version, accepted, accepted != options.Value.Version);
    }
}

public sealed record SaveBreachRequest(
    string Title,
    string Description,
    DateTimeOffset DiscoveredAt,
    DateTimeOffset? OccurredAt,
    string? DataInvolved,
    int? PeopleAffected,
    bool SpecialInformation,
    string? Containment,
    DateTimeOffset? RegulatorNotifiedAt,
    DateTimeOffset? PeopleNotifiedAt);

public sealed record BreachDto(
    Guid Id,
    string Title,
    string Description,
    DateTimeOffset DiscoveredAt,
    DateTimeOffset? OccurredAt,
    string? DataInvolved,
    int? PeopleAffected,
    bool SpecialInformation,
    string? Containment,
    DateTimeOffset? RegulatorNotifiedAt,
    DateTimeOffset? PeopleNotifiedAt,
    BreachStatus Status,
    bool NotificationOverdue,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ClosedAt);

/// <summary>The breach register. Everything here is audited: it is the church's evidence of how it responded.</summary>
public sealed class BreachService(IPrivacyDb db, ICurrentUser currentUser, IAuditLog audit, TimeProvider clock)
{
    private static readonly Error NotFound = Error.NotFound("privacy.breach_not_found", "Breach not found.");

    public async Task<IReadOnlyList<BreachDto>> ListAsync(CancellationToken cancellationToken)
    {
        var rows = await db.Breaches.AsNoTracking().OrderBy(b => b.Status).ThenByDescending(b => b.DiscoveredAt).Take(200).ToListAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord("privacy.breaches.viewed", "breach", null, Details: new { count = rows.Count }, IsSensitiveRead: true), cancellationToken);
        var now = clock.GetUtcNow();
        return rows.Select(b => ToDto(b, now)).ToList();
    }

    public async Task<Result<BreachDto>> RecordAsync(SaveBreachRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } me)
        {
            return Error.Unauthorized("privacy.sign_in", "Sign in first.");
        }

        var now = clock.GetUtcNow();
        var breach = Breach.Record(request.Title, request.Description, request.DiscoveredAt, me, now);
        Apply(breach, request, now);
        db.Breaches.Add(breach);
        await db.SaveChangesAsync(cancellationToken);
        await AuditAsync("privacy.breach.recorded", breach, cancellationToken);
        return ToDto(breach, now);
    }

    public Task<Result<BreachDto>> UpdateAsync(Guid id, SaveBreachRequest request, CancellationToken cancellationToken) =>
        ChangeAsync(id, "privacy.breach.updated", (b, now) => Apply(b, request, now), cancellationToken);

    public Task<Result<BreachDto>> CloseAsync(Guid id, CancellationToken cancellationToken) =>
        ChangeAsync(id, "privacy.breach.closed", (b, now) => b.Close(now), cancellationToken);

    public Task<Result<BreachDto>> ReopenAsync(Guid id, CancellationToken cancellationToken) =>
        ChangeAsync(id, "privacy.breach.reopened", (b, now) => b.Reopen(now), cancellationToken);

    private async Task<Result<BreachDto>> ChangeAsync(Guid id, string action, Action<Breach, DateTimeOffset> change, CancellationToken cancellationToken)
    {
        var breach = await db.Breaches.SingleOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (breach is null)
        {
            return NotFound;
        }

        var now = clock.GetUtcNow();
        change(breach, now);
        await db.SaveChangesAsync(cancellationToken);
        await AuditAsync(action, breach, cancellationToken);
        return ToDto(breach, now);
    }

    private static void Apply(Breach b, SaveBreachRequest r, DateTimeOffset now) =>
        b.Update(r.Title, r.Description, r.OccurredAt, r.DataInvolved, r.PeopleAffected, r.SpecialInformation, r.Containment, r.RegulatorNotifiedAt, r.PeopleNotifiedAt, now);

    private Task AuditAsync(string action, Breach b, CancellationToken cancellationToken) =>
        audit.RecordAsync(new AuditRecord(action, "breach", b.Id.ToString(), Details: new { b.Title, status = b.Status.ToString() }), cancellationToken);

    private static BreachDto ToDto(Breach b, DateTimeOffset now) => new(
        b.Id, b.Title, b.Description, b.DiscoveredAt, b.OccurredAt, b.DataInvolved, b.PeopleAffected, b.SpecialInformation, b.Containment,
        b.RegulatorNotifiedAt, b.PeopleNotifiedAt, b.Status, b.NotificationOverdue(now), b.CreatedAt, b.UpdatedAt, b.ClosedAt);
}
