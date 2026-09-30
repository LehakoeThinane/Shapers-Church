using Microsoft.EntityFrameworkCore;
using Shapers.Church.Contracts;
using Shapers.Communications.Contracts;
using Shapers.Communications.Domain;
using Shapers.People.Contracts;
using Shapers.Platform.Auditing;
using Shapers.Platform.Authorization;

namespace Shapers.Communications.Application;

public sealed record SaveAnnouncementRequest(string Title, string Body, string? Link, string Scope, bool SendEmail, DateTimeOffset? SendAt);

public sealed record ReturnAnnouncementRequest(string? Note);

/// <summary>Roughly who an announcement will reach, so staff can check the audience before it goes out.</summary>
public sealed record AudienceDto(int People, int WithApp, int ByEmail);

public sealed record DeliveryCountsDto(int Inbox, int Pushed, int Emailed, int Failed);

public sealed record AnnouncementDto(
    Guid Id,
    string Title,
    string Body,
    string? Link,
    string Scope,
    bool SendEmail,
    DateTimeOffset? SendAt,
    AnnouncementStatus Status,
    bool NeedsApproval,
    bool CreatedByMe,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ApprovedAt,
    string? ReturnNote,
    DateTimeOffset? SentAt,
    AudienceDto? Audience,
    DeliveryCountsDto? Delivered);

/// <summary>
/// Staff write announcements for a scope. Writing needs announcements.send there; anything for a whole campus or the
/// whole church also needs a second person with announcements.approve.
/// </summary>
public sealed class AnnouncementService(
    ICommunicationsDb db,
    IPeopleDirectory people,
    IChurchDirectory church,
    IAuthorizer authorizer,
    ICurrentUser currentUser,
    IAuditLog audit,
    TimeProvider clock)
{
    private static readonly Error NotFound = Error.NotFound("communications.announcement_not_found", "Announcement not found.");

    public async Task<IReadOnlyList<AnnouncementDto>> ListAsync(CancellationToken cancellationToken)
    {
        var scopes = (await authorizer.ScopesForAsync(CommunicationsPermissions.AnnouncementsSend, cancellationToken))
            .Concat(await authorizer.ScopesForAsync(CommunicationsPermissions.AnnouncementsApprove, cancellationToken))
            .ToList();
        var rows = await db.Announcements.AsNoTracking().WithinScopes(a => a.Scope, scopes)
            .OrderByDescending(a => a.UpdatedAt)
            .Take(100)
            .ToListAsync(cancellationToken);
        return rows.Select(a => ToDto(a, null, null)).ToList();
    }

    public async Task<Result<AnnouncementDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var a = await FindVisibleAsync(id, cancellationToken);
        return a is null ? NotFound : await DetailAsync(a, cancellationToken);
    }

    public async Task<Result<AnnouncementDto>> CreateAsync(SaveAnnouncementRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } me)
        {
            return Error.Unauthorized("communications.sign_in", "Sign in first.");
        }

        var scope = await CheckScopeAsync(request.Scope, cancellationToken);
        if (scope.IsFailure)
        {
            return scope.Error!;
        }

        var a = Announcement.Draft(request.Title, request.Body, request.Link, scope.Value, request.SendEmail, request.SendAt, me, clock.GetUtcNow());
        db.Announcements.Add(a);
        await db.SaveChangesAsync(cancellationToken);
        await AuditAsync("communications.announcement.created", a, cancellationToken);
        return await DetailAsync(a, cancellationToken);
    }

    public async Task<Result<AnnouncementDto>> UpdateAsync(Guid id, SaveAnnouncementRequest request, CancellationToken cancellationToken)
    {
        var a = await FindForSendAsync(id, cancellationToken);
        if (a is null)
        {
            return NotFound;
        }

        var scope = await CheckScopeAsync(request.Scope, cancellationToken);
        if (scope.IsFailure)
        {
            return scope.Error!;
        }

        a.Edit(request.Title, request.Body, request.Link, scope.Value, request.SendEmail, request.SendAt, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await AuditAsync("communications.announcement.edited", a, cancellationToken);
        return await DetailAsync(a, cancellationToken);
    }

    public Task<Result<AnnouncementDto>> SubmitAsync(Guid id, CancellationToken cancellationToken) =>
        ChangeAsync(id, CommunicationsPermissions.AnnouncementsSend, "communications.announcement.submitted", (a, _, now) => a.Submit(now), cancellationToken);

    public Task<Result<AnnouncementDto>> ApproveAsync(Guid id, CancellationToken cancellationToken) =>
        ChangeAsync(id, CommunicationsPermissions.AnnouncementsApprove, "communications.announcement.approved", (a, me, now) => a.Approve(me, now), cancellationToken);

    /// <summary>An approver sends it back with a note, or the author pulls it back to fix something.</summary>
    public async Task<Result<AnnouncementDto>> ReturnToDraftAsync(Guid id, ReturnAnnouncementRequest request, CancellationToken cancellationToken)
    {
        var a = await FindVisibleAsync(id, cancellationToken);
        if (a is null)
        {
            return NotFound;
        }

        var scope = ScopePath.Parse(a.Scope);
        var mine = a.CreatedByUserId == currentUser.UserId && await authorizer.CanAsync(CommunicationsPermissions.AnnouncementsSend, scope, cancellationToken);
        if (!mine && !await authorizer.CanAsync(CommunicationsPermissions.AnnouncementsApprove, scope, cancellationToken))
        {
            return NotFound;
        }

        a.ReturnToDraft(request.Note, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await AuditAsync("communications.announcement.returned", a, cancellationToken);
        return await DetailAsync(a, cancellationToken);
    }

    public Task<Result<AnnouncementDto>> CancelAsync(Guid id, CancellationToken cancellationToken) =>
        ChangeAsync(id, CommunicationsPermissions.AnnouncementsSend, "communications.announcement.cancelled", (a, _, now) => a.Cancel(now), cancellationToken);

    private async Task<Result<AnnouncementDto>> ChangeAsync(
        Guid id, string permission, string action, Action<Announcement, Guid, DateTimeOffset> change, CancellationToken cancellationToken)
    {
        var a = await db.Announcements.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (a is null || currentUser.UserId is not { } me || !await authorizer.CanAsync(permission, ScopePath.Parse(a.Scope), cancellationToken))
        {
            return NotFound;
        }

        change(a, me, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await AuditAsync(action, a, cancellationToken);
        return await DetailAsync(a, cancellationToken);
    }

    private async Task<Announcement?> FindVisibleAsync(Guid id, CancellationToken cancellationToken)
    {
        var a = await db.Announcements.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (a is null)
        {
            return null;
        }

        var scope = ScopePath.Parse(a.Scope);
        return await authorizer.CanAsync(CommunicationsPermissions.AnnouncementsSend, scope, cancellationToken)
            || await authorizer.CanAsync(CommunicationsPermissions.AnnouncementsApprove, scope, cancellationToken)
            ? a
            : null;
    }

    private async Task<Announcement?> FindForSendAsync(Guid id, CancellationToken cancellationToken)
    {
        var a = await db.Announcements.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        return a is not null && await authorizer.CanAsync(CommunicationsPermissions.AnnouncementsSend, ScopePath.Parse(a.Scope), cancellationToken) ? a : null;
    }

    private async Task<Result<ScopePath>> CheckScopeAsync(string? value, CancellationToken cancellationToken)
    {
        if (!ScopePath.TryParse(value, out var scope) || !await church.ScopeExistsAsync(scope.Value, cancellationToken))
        {
            return new Error("communications.scope_invalid", "Choose the church, a campus or a ministry.");
        }

        return await authorizer.CanAsync(CommunicationsPermissions.AnnouncementsSend, scope, cancellationToken)
            ? scope
            : Error.Forbidden("communications.forbidden", "You can't send announcements there.");
    }

    private async Task<AnnouncementDto> DetailAsync(Announcement a, CancellationToken cancellationToken)
    {
        AudienceDto? audience = null;
        DeliveryCountsDto? delivered = null;
        if (a.Status == AnnouncementStatus.Sent)
        {
            var counts = await db.Notifications.AsNoTracking()
                .Where(n => n.SourceKey == a.SourceKey)
                .Select(n => new
                {
                    Pushed = n.Deliveries.Any(d => d.Channel == Channel.Push && d.Status == DeliveryStatus.Sent),
                    Emailed = n.Deliveries.Any(d => d.Channel == Channel.Email && d.Status == DeliveryStatus.Sent),
                    Failed = n.Deliveries.Any(d => d.Status == DeliveryStatus.Failed),
                })
                .ToListAsync(cancellationToken);
            delivered = new DeliveryCountsDto(counts.Count, counts.Count(c => c.Pushed), counts.Count(c => c.Emailed), counts.Count(c => c.Failed));
        }
        else if (a.Status != AnnouncementStatus.Cancelled)
        {
            var everyone = await people.InScopeAsync(a.Scope, cancellationToken);
            var ids = everyone.Select(p => p.Id).ToList();
            var withApp = await db.Devices.AsNoTracking().Where(d => d.DisabledAt == null && ids.Contains(d.PersonId)).Select(d => d.PersonId).Distinct().CountAsync(cancellationToken);
            var byEmail = a.SendEmail
                ? (await people.WithConsentAsync(everyone.Where(p => p.Email is not null).Select(p => p.Id).ToList(), CommunicationConsents.Email, cancellationToken)).Count
                : 0;
            audience = new AudienceDto(ids.Count, withApp, byEmail);
        }

        return ToDto(a, audience, delivered);
    }

    private AnnouncementDto ToDto(Announcement a, AudienceDto? audience, DeliveryCountsDto? delivered) => new(
        a.Id, a.Title, a.Body, a.Link, a.Scope, a.SendEmail, a.SendAt, a.Status, a.NeedsApproval, a.CreatedByUserId == currentUser.UserId,
        a.CreatedAt, a.UpdatedAt, a.ApprovedAt, a.ReturnNote, a.SentAt, audience, delivered);

    private Task AuditAsync(string action, Announcement a, CancellationToken cancellationToken) =>
        audit.RecordAsync(new AuditRecord(action, "announcement", a.Id.ToString(), ScopePath.Parse(a.Scope), new { a.Title, status = a.Status.ToString() }), cancellationToken);
}

/// <summary>Every minute: announcements that are approved (or need no approval) and due go out.</summary>
public sealed class AnnouncementDispatchJob(ICommunicationsDb db, Notifier notifier, TimeProvider clock)
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var queued = await db.Announcements.Where(a => a.Status == AnnouncementStatus.Queued).ToListAsync(cancellationToken);
        var sent = 0;
        foreach (var a in queued.Where(a => a.IsDue(now)))
        {
            var recipients = await notifier.ToScopeAsync(a.Scope, new Notifier.Message(Topic.Announcements, a.Title, a.Body, a.Link, a.SourceKey, Email: a.SendEmail), cancellationToken);
            a.MarkSent(recipients, now);
            await db.SaveChangesAsync(cancellationToken);
            sent++;
        }

        return sent;
    }
}
