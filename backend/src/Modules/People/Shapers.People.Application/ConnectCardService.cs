using Microsoft.EntityFrameworkCore;
using Shapers.People.Contracts;
using Shapers.People.Domain;
using Shapers.Platform.Auditing;
using Shapers.Platform.Authorization;

namespace Shapers.People.Application;

public sealed record ConnectCardRequest(
    string? FirstName,
    string? LastName,
    string? Mobile,
    string? Email,
    IReadOnlyList<ConnectReason> Reasons,
    string? Message,
    string Source,
    Guid? SourceId,
    bool ConsentToKeepDetails,
    string? PolicyVersion);

public sealed record ConnectCardReceipt(Guid Id);

public sealed record ConnectCardDto(
    Guid Id,
    Guid PersonId,
    string PersonName,
    string? Mobile,
    string? Email,
    IReadOnlyList<ConnectReason> Reasons,
    string? Message,
    string Source,
    ConnectCardStatus Status,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? HandledAt,
    string? HandlerNote,
    bool IsNewPerson);

public sealed record HandleConnectCardRequest(string? Note);

public sealed class ConnectCardService(
    IPeopleDb db,
    ICurrentUser currentUser,
    IAuthorizer authorizer,
    IGuestRecords guests,
    IAuditLog audit,
    TimeProvider clock)
{
    /// <summary>
    /// Members' cards go on their own record. A guest's details always create a new record (flagged for
    /// duplicate review), never attach to an existing one: anyone can type anyone's phone number.
    /// </summary>
    public async Task<Result<ConnectCardReceipt>> SubmitAsync(ConnectCardRequest request, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        Person? person = null;
        if (currentUser.PersonId is { } personId)
        {
            person = await db.Persons.SingleOrDefaultAsync(p => p.Id == personId && p.Status != PersonStatus.Merged, cancellationToken);
        }

        if (person is null)
        {
            var guest = await guests.CreateAsync(
                new GuestDetails(request.FirstName, request.LastName, request.Mobile, request.Email, EmailVerified: false, request.ConsentToKeepDetails,
                    GuestOrigin.ConnectCard, FromWebsite: request.Source == "website", request.PolicyVersion),
                cancellationToken);
            if (guest.IsFailure)
            {
                return guest.Error!;
            }

            person = await db.Persons.SingleAsync(p => p.Id == guest.Value, cancellationToken);
        }

        var card = ConnectCard.Submit(person.Id, ScopePath.Parse(person.Scope), request.Reasons, request.Message, request.Source, request.SourceId, now);
        db.ConnectCards.Add(card);
        await db.SaveChangesAsync(cancellationToken);
        return new ConnectCardReceipt(card.Id);
    }

    public async Task<IReadOnlyList<ConnectCardDto>> ListAsync(ConnectCardStatus? status, CancellationToken cancellationToken)
    {
        var scopes = await authorizer.ScopesForAsync(PeoplePermissions.ProfilesView, cancellationToken);
        var query = db.ConnectCards.AsNoTracking().WithinScopes(c => c.Scope, scopes);
        if (status is { } s)
        {
            query = query.Where(c => c.Status == s);
        }

        var cards = await query.OrderByDescending(c => c.SubmittedAt).Take(200).ToListAsync(cancellationToken);
        var personIds = cards.Select(c => c.PersonId).Distinct().ToList();
        var people = await db.Persons.AsNoTracking().Where(p => personIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);

        // Cards can include prayer needs and decisions of faith: viewing them is a sensitive read.
        await audit.RecordAsync(new AuditRecord("people.connect_cards.viewed", "connect_card", null, Details: new { count = cards.Count }, IsSensitiveRead: true), cancellationToken);

        return cards.Select(c =>
        {
            var p = people.GetValueOrDefault(c.PersonId);
            return new ConnectCardDto(
                c.Id,
                c.PersonId,
                p?.DisplayName ?? "Unknown",
                p?.PrimaryContact(ContactType.Mobile)?.Value,
                p?.PrimaryContact(ContactType.Email)?.Value,
                c.Reasons,
                c.Message,
                c.Source,
                c.Status,
                c.SubmittedAt,
                c.HandledAt,
                c.HandlerNote,
                p?.Source == PersonSource.VisitorCard && p.CreatedAt >= c.SubmittedAt.AddMinutes(-1));
        }).ToList();
    }

    public async Task<Result> HandleAsync(Guid id, HandleConnectCardRequest request, CancellationToken cancellationToken)
    {
        var card = await db.ConnectCards.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (card is null || !await authorizer.CanAsync(PeoplePermissions.ProfilesEdit, ScopePath.Parse(card.Scope), cancellationToken))
        {
            return Error.NotFound("people.card_not_found", "Connect card not found.");
        }

        card.MarkHandled(currentUser.UserId, request.Note, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord("people.connect_card.handled", "connect_card", id.ToString(), ScopePath.Parse(card.Scope)), cancellationToken);
        return Result.Success();
    }
}
