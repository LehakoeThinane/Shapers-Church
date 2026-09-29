using Microsoft.EntityFrameworkCore;
using Shapers.Church.Contracts;
using Shapers.People.Contracts;
using Shapers.People.Domain;
using Shapers.Platform.Auditing;
using Shapers.Platform.Authorization;
using Shapers.Platform.Text;

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
    IChurchDirectory church,
    DuplicateDetector duplicates,
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

        var created = false;
        if (person is null)
        {
            var guest = await CreateGuestAsync(request, now, cancellationToken);
            if (guest.IsFailure)
            {
                return guest.Error!;
            }

            person = guest.Value;
            created = true;
        }

        var card = ConnectCard.Submit(person.Id, ScopePath.Parse(person.Scope), request.Reasons, request.Message, request.Source, request.SourceId, now);
        db.ConnectCards.Add(card);
        await db.SaveChangesAsync(cancellationToken);
        if (created)
        {
            await duplicates.DetectAsync(person, cancellationToken);
        }

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

    private async Task<Result<Person>> CreateGuestAsync(ConnectCardRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName))
        {
            return new Error("people.name_required", "Please tell us your first and last name.");
        }

        string? mobile = null;
        if (!string.IsNullOrWhiteSpace(request.Mobile) && !ContactNormaliser.TryNormalisePhone(request.Mobile, out mobile))
        {
            return new Error("people.mobile_invalid", "That mobile number doesn't look right.");
        }

        string? email = null;
        if (!string.IsNullOrWhiteSpace(request.Email) && !ContactNormaliser.TryNormaliseEmail(request.Email, out email))
        {
            return new Error("people.email_invalid", "That email address doesn't look right.");
        }

        if (mobile is null && email is null)
        {
            return new Error("people.contact_required", "Give us a mobile number or email so we can get back to you.");
        }

        if (!request.ConsentToKeepDetails)
        {
            return new Error("people.consent_required", "We need your permission to keep your details so the church can contact you.");
        }

        var campuses = await church.GetCampusesAsync(cancellationToken);
        var campus = campuses.FirstOrDefault(c => c.IsPrimary) ?? (campuses.Count > 0 ? campuses[0] : null);
        var scope = ScopePath.Parse(campus?.Scope ?? (await church.GetRootScopeAsync(cancellationToken)).Path);
        var status = await db.MembershipStatuses.SingleAsync(s => s.IsDefault, cancellationToken);

        var person = Person.Create(scope, request.FirstName, request.LastName, status, PersonSource.VisitorCard, now);
        if (mobile is not null)
        {
            person.AddContact(ContactType.Mobile, mobile, isPrimary: true, isVerified: false, now);
        }

        if (email is not null)
        {
            person.AddContact(ContactType.Email, email, isPrimary: true, isVerified: false, now);
        }

        db.Persons.Add(person);
        db.ConsentRecords.Add(ConsentRecord.Record(
            person.Id,
            ConsentPurposes.ChurchRecord,
            granted: true,
            LawfulBasis.Consent,
            string.IsNullOrWhiteSpace(request.PolicyVersion) ? "2026-09" : request.PolicyVersion,
            request.Source == "website" ? ConsentSource.Website : ConsentSource.MobileApp,
            now,
            recordedBy: null));
        return person;
    }
}
