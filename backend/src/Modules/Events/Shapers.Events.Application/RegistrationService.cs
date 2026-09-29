using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Shapers.Events.Contracts;
using Shapers.Events.Domain;
using Shapers.People.Contracts;
using Shapers.Platform.Auditing;
using Shapers.Platform.Authorization;
using Shapers.Platform.Security;
using Shapers.Platform.Text;

namespace Shapers.Events.Application;

public sealed class RegistrationService(
    IEventsDb db,
    EventReader reader,
    EventEmails emails,
    IPeopleDirectory people,
    IGuestRecords guests,
    IKeyedHasher hasher,
    IAuthorizer authorizer,
    ICurrentUser currentUser,
    IAuditLog audit,
    TimeProvider clock)
{
    private const string CodePurpose = "events.email-code";
    private const string KeyPurpose = "events.guest-key";
    private static readonly string[] CsvHeaders = ["Name", "Registered by", "Email", "Status", "Registered", "Checked in"];
    private static readonly Error EventNotFound = Error.NotFound("events.not_found", "Event not found.");
    private static readonly Error RegistrationNotFound = Error.NotFound("events.registration_not_found", "Registration not found.");

    // ---------- Members ----------

    /// <summary>A signed-in member registers themselves and, optionally, people in their household.</summary>
    public async Task<Result<RegistrationDto>> RegisterMemberAsync(string slug, MemberRegisterRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.PersonId is not { } me)
        {
            return Error.Unauthorized("events.sign_in", "Sign in to register.");
        }

        var e = await db.Events.AsNoTracking().SingleOrDefaultAsync(x => x.Slug == slug, cancellationToken);
        if (e is null)
        {
            return EventNotFound;
        }

        var ids = request.AttendeePersonIds.Count == 0 ? [me] : request.AttendeePersonIds.Distinct().ToList();
        var household = (await people.GetHouseholdMembersAsync(me, cancellationToken)).Select(h => h.PersonId).ToHashSet();
        if (ids.Any(id => id != me && !household.Contains(id)))
        {
            return Error.Forbidden("events.not_household", "You can register yourself and people in your household.");
        }

        var names = await people.GetManyAsync(ids, cancellationToken);
        var attendees = ids.Select(id => ((Guid?)id, names.GetValueOrDefault(id)?.DisplayName ?? "Guest")).ToList();
        var placed = await PlaceAsync(e.Id, me, attendees, request.Answers, RegistrationSource.App, null, cancellationToken);
        if (placed.IsFailure)
        {
            return placed.Error!;
        }

        return await reader.ToDtoAsync(placed.Value.Registration, placed.Value.Event, cancellationToken);
    }

    public async Task<IReadOnlyList<RegistrationDto>> MineAsync(CancellationToken cancellationToken)
    {
        if (currentUser.PersonId is not { } me)
        {
            return [];
        }

        var since = clock.GetUtcNow().AddDays(-1);
        var registrations = await db.Registrations.AsNoTracking()
            .Where(r => r.Status != RegistrationStatus.Cancelled && (r.RegistrantPersonId == me || r.Attendees.Any(a => a.PersonId == me)))
            .ToListAsync(cancellationToken);
        var eventIds = registrations.Select(r => r.EventId).Distinct().ToList();
        var events = await db.Events.AsNoTracking().Where(e => eventIds.Contains(e.Id) && e.EndsAt >= since).ToDictionaryAsync(e => e.Id, cancellationToken);

        var result = new List<RegistrationDto>();
        foreach (var r in registrations.Where(r => events.ContainsKey(r.EventId)).OrderBy(r => events[r.EventId].StartsAt))
        {
            result.Add(await reader.ToDtoAsync(r, events[r.EventId], cancellationToken));
        }

        return result;
    }

    public async Task<Result> CancelMineAsync(Guid registrationId, CancellationToken cancellationToken)
    {
        var owner = await db.Registrations.AsNoTracking()
            .Where(r => r.Id == registrationId)
            .Select(r => new { r.RegistrantPersonId })
            .SingleOrDefaultAsync(cancellationToken);
        return owner is null || owner.RegistrantPersonId != currentUser.PersonId
            ? RegistrationNotFound
            : await CancelAndPromoteAsync(registrationId, cancellationToken);
    }

    // ---------- Guests ----------

    /// <summary>Emails a guest a six-digit code, proving the address before we create a record for them.</summary>
    public async Task<Result<GuestCodeResponse>> SendGuestCodeAsync(string slug, GuestCodeRequest request, CancellationToken cancellationToken)
    {
        var e = await db.Events.AsNoTracking().SingleOrDefaultAsync(x => x.Slug == slug, cancellationToken);
        if (e is null || e.Visibility != EventVisibility.Public)
        {
            return EventNotFound;
        }

        if (e.RegistrationClosedReason(clock.GetUtcNow()) is { } closed)
        {
            return new Error("events.registration_closed", closed);
        }

        if (!ContactNormaliser.TryNormaliseEmail(request.Email, out var email))
        {
            return new Error("events.email_invalid", "Enter a valid email address.");
        }

        var now = clock.GetUtcNow();
        var recent = await db.EmailVerifications.CountAsync(v => v.Email == email && v.CreatedAt > now.AddMinutes(-15), cancellationToken);
        if (recent >= 3)
        {
            return new Error("events.too_many_codes", "Too many codes sent to this address. Please wait a few minutes.", ErrorKind.RateLimited);
        }

        var id = Guid.CreateVersion7();
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        var verification = EmailVerification.Create(id, email, hasher.Hash(CodePurpose, $"{id:N}:{code}"), now);
        db.EmailVerifications.Add(verification);
        await db.SaveChangesAsync(cancellationToken);
        await emails.SendGuestCodeAsync(email, e.Title, code, cancellationToken);
        return new GuestCodeResponse(id, verification.ExpiresAt);
    }

    /// <summary>
    /// A guest registers with a verified email. A new church record is always created (flagged for duplicate
    /// review), and the guest gets a private link to view or cancel their booking.
    /// </summary>
    public async Task<Result<GuestRegistrationReceipt>> RegisterGuestAsync(string slug, GuestRegisterRequest request, CancellationToken cancellationToken)
    {
        var e = await db.Events.AsNoTracking().SingleOrDefaultAsync(x => x.Slug == slug, cancellationToken);
        if (e is null || e.Visibility != EventVisibility.Public)
        {
            return EventNotFound;
        }

        var verification = await db.EmailVerifications.SingleOrDefaultAsync(v => v.Id == request.VerificationId, cancellationToken);
        var codeOk = verification is not null
            && ContactNormaliser.TryNormaliseEmail(request.Email, out var email)
            && email == verification.Email
            && verification.TryUse(hasher.Hash(CodePurpose, $"{verification.Id:N}:{request.Code?.Trim()}"), clock.GetUtcNow());
        if (verification is not null)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        if (!codeOk)
        {
            return Error.Unauthorized("events.code_invalid", "That code isn't right or has expired. Request a new one.");
        }

        // Check the event can take the booking before creating a record for someone.
        if (e.RegistrationClosedReason(clock.GetUtcNow()) is { } closed)
        {
            return new Error("events.registration_closed", closed);
        }

        var person = await guests.CreateAsync(
            new GuestDetails(request.FirstName, request.LastName, request.Mobile, request.Email, EmailVerified: true, request.ConsentToKeepDetails,
                GuestOrigin.EventRegistration, FromWebsite: true, request.PolicyVersion),
            cancellationToken);
        if (person.IsFailure)
        {
            return person.Error!;
        }

        var attendees = new List<(Guid?, string)> { (person.Value, $"{request.FirstName.Trim()} {request.LastName.Trim()}") };
        attendees.AddRange((request.OtherAttendeeNames ?? []).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => ((Guid?)null, n.Trim())));

        var key = hasher.NewSecret();
        var placed = await PlaceAsync(e.Id, person.Value, attendees, request.Answers, RegistrationSource.Web, hasher.Hash(KeyPurpose, key), cancellationToken);
        if (placed.IsFailure)
        {
            return placed.Error!;
        }

        var dto = await reader.ToDtoAsync(placed.Value.Registration, placed.Value.Event, cancellationToken);
        await emails.SendConfirmationAsync(request.Email, dto, placed.Value.Event, key, cancellationToken);
        return new GuestRegistrationReceipt(dto, key);
    }

    public async Task<Result<RegistrationDto>> GetGuestAsync(Guid registrationId, string key, CancellationToken cancellationToken)
    {
        var r = await FindGuestAsync(registrationId, key, cancellationToken);
        if (r is null)
        {
            return RegistrationNotFound;
        }

        var e = await db.Events.AsNoTracking().SingleAsync(x => x.Id == r.EventId, cancellationToken);
        return await reader.ToDtoAsync(r, e, cancellationToken);
    }

    public async Task<Result> CancelGuestAsync(Guid registrationId, string key, CancellationToken cancellationToken) =>
        await FindGuestAsync(registrationId, key, cancellationToken) is null
            ? RegistrationNotFound
            : await CancelAndPromoteAsync(registrationId, cancellationToken);

    // ---------- Staff ----------

    public async Task<Result<IReadOnlyList<AttendeeRowDto>>> AttendeesAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var e = await db.Events.AsNoTracking().SingleOrDefaultAsync(x => x.Id == eventId, cancellationToken);
        if (e is null || !await authorizer.CanAsync(EventsPermissions.RegistrationsView, ScopePath.Parse(e.Scope), cancellationToken))
        {
            return EventNotFound;
        }

        var rows = await RowsAsync(e, cancellationToken);
        await audit.RecordAsync(new AuditRecord("events.registrations.viewed", "event", e.Id.ToString(), ScopePath.Parse(e.Scope), new { count = rows.Count }, IsSensitiveRead: true), cancellationToken);
        return Result<IReadOnlyList<AttendeeRowDto>>.Ok(rows);
    }

    /// <summary>A CSV of attendees. Exports leave the system, so each one is audited.</summary>
    public async Task<Result<string>> ExportCsvAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var e = await db.Events.AsNoTracking().SingleOrDefaultAsync(x => x.Id == eventId, cancellationToken);
        if (e is null || !await authorizer.CanAsync(EventsPermissions.RegistrationsView, ScopePath.Parse(e.Scope), cancellationToken))
        {
            return EventNotFound;
        }

        var rows = await RowsAsync(e, cancellationToken);
        var questions = e.Questions.OrderBy(q => q.Order).Select(q => q.Label).ToList();
        var csv = new StringBuilder();
        csv.AppendLine(string.Join(',', CsvHeaders.Concat(questions).Select(Cell)));
        foreach (var row in rows)
        {
            var cells = new[]
            {
                row.Name, row.RegistrantName, row.RegistrantEmail ?? string.Empty, row.Status.ToString(),
                row.RegisteredAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                row.CheckedInAt?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? string.Empty,
            }.Concat(questions.Select(q => row.Answers.GetValueOrDefault(q) ?? string.Empty));
            csv.AppendLine(string.Join(',', cells.Select(Cell)));
        }

        await audit.RecordAsync(new AuditRecord("events.registrations.exported", "event", e.Id.ToString(), ScopePath.Parse(e.Scope), new { count = rows.Count }, IsSensitiveRead: true), cancellationToken);
        return csv.ToString();
    }

    /// <summary>Staff register someone who phoned in. A new person needs their verbal consent to keep their details.</summary>
    public async Task<Result<RegistrationDto>> RegisterByStaffAsync(Guid eventId, AdminRegisterRequest request, CancellationToken cancellationToken)
    {
        var e = await db.Events.AsNoTracking().SingleOrDefaultAsync(x => x.Id == eventId, cancellationToken);
        if (e is null || !await authorizer.CanAsync(EventsPermissions.RegistrationsManage, ScopePath.Parse(e.Scope), cancellationToken))
        {
            return EventNotFound;
        }

        Guid personId;
        string name;
        if (request.PersonId is { } existing)
        {
            var summary = await people.GetAsync(existing, cancellationToken);
            if (summary is null)
            {
                return Error.NotFound("events.person_not_found", "Person not found.");
            }

            (personId, name) = (summary.Id, summary.DisplayName);
        }
        else
        {
            var created = await guests.CreateAsync(
                new GuestDetails(request.FirstName, request.LastName, request.Mobile, request.Email, EmailVerified: false, request.ConsentGivenVerbally,
                    GuestOrigin.EventRegistration, FromWebsite: false, null),
                cancellationToken);
            if (created.IsFailure)
            {
                return created.Error!;
            }

            (personId, name) = (created.Value, $"{request.FirstName?.Trim()} {request.LastName?.Trim()}");
        }

        var attendees = new List<(Guid?, string)> { (personId, name) };
        attendees.AddRange((request.OtherAttendeeNames ?? []).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => ((Guid?)null, n.Trim())));
        var placed = await PlaceAsync(e.Id, personId, attendees, request.Answers, RegistrationSource.Admin, null, cancellationToken);
        if (placed.IsFailure)
        {
            return placed.Error!;
        }

        await audit.RecordAsync(new AuditRecord("events.registration.created_by_staff", "registration", placed.Value.Registration.Id.ToString(), ScopePath.Parse(e.Scope)), cancellationToken);
        return await reader.ToDtoAsync(placed.Value.Registration, placed.Value.Event, cancellationToken);
    }

    public async Task<Result> CancelByStaffAsync(Guid registrationId, CancellationToken cancellationToken)
    {
        var info = await db.Registrations.AsNoTracking()
            .Where(r => r.Id == registrationId)
            .Join(db.Events, r => r.EventId, e => e.Id, (r, e) => new { e.Scope })
            .SingleOrDefaultAsync(cancellationToken);
        if (info is null || !await authorizer.CanAsync(EventsPermissions.RegistrationsManage, ScopePath.Parse(info.Scope), cancellationToken))
        {
            return RegistrationNotFound;
        }

        var result = await CancelAndPromoteAsync(registrationId, cancellationToken);
        if (result.IsSuccess)
        {
            await audit.RecordAsync(new AuditRecord("events.registration.cancelled_by_staff", "registration", registrationId.ToString(), ScopePath.Parse(info.Scope)), cancellationToken);
        }

        return result;
    }

    /// <summary>Scan a ticket, or pick a name from the door list. Works for anyone with check-in rights at the event's scope.</summary>
    public async Task<Result<CheckInResultDto>> CheckInAsync(Guid eventId, CheckInRequest request, CancellationToken cancellationToken)
    {
        var e = await db.Events.AsNoTracking().SingleOrDefaultAsync(x => x.Id == eventId, cancellationToken);
        if (e is null || !await authorizer.CanAsync(EventsPermissions.CheckIn, ScopePath.Parse(e.Scope), cancellationToken))
        {
            return EventNotFound;
        }

        Registration? registration;
        Guid attendeeId;
        if (!string.IsNullOrWhiteSpace(request.TicketCode))
        {
            var code = TicketCode.Normalise(request.TicketCode);
            registration = await db.Registrations.SingleOrDefaultAsync(r => r.EventId == eventId && r.Attendees.Any(a => a.TicketCode == code), cancellationToken);
            attendeeId = registration?.Attendees.Single(a => a.TicketCode == code).Id ?? Guid.Empty;
        }
        else if (request.AttendeeId is { } id)
        {
            registration = await db.Registrations.SingleOrDefaultAsync(r => r.EventId == eventId && r.Attendees.Any(a => a.Id == id), cancellationToken);
            attendeeId = id;
        }
        else
        {
            return new Error("events.checkin_missing", "Scan a ticket or choose a name.");
        }

        if (registration is null)
        {
            return Error.NotFound("events.ticket_not_found", "This ticket isn't for this event.");
        }

        var (outcome, attendee) = registration.CheckIn(attendeeId, currentUser.UserId, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        var message = outcome switch
        {
            CheckInOutcome.CheckedIn => $"Welcome, {attendee.Name}!",
            CheckInOutcome.AlreadyCheckedIn => $"{attendee.Name} already checked in at {attendee.CheckedInAt:HH:mm}.",
            _ => registration.Status == RegistrationStatus.Waitlisted ? $"{attendee.Name} is on the waiting list, not confirmed." : $"{attendee.Name}'s registration was cancelled.",
        };
        return new CheckInResultDto(outcome, attendee.Name, attendee.CheckedInAt, message);
    }

    /// <summary>Names only, for the door list: enough to check someone in, nothing more.</summary>
    public async Task<Result<IReadOnlyList<TicketDto>>> DoorListAsync(Guid eventId, string? search, CancellationToken cancellationToken)
    {
        var e = await db.Events.AsNoTracking().SingleOrDefaultAsync(x => x.Id == eventId, cancellationToken);
        if (e is null || !await authorizer.CanAsync(EventsPermissions.CheckIn, ScopePath.Parse(e.Scope), cancellationToken))
        {
            return EventNotFound;
        }

        var attendees = await db.Registrations.AsNoTracking()
            .Where(r => r.EventId == eventId && r.Status == RegistrationStatus.Confirmed)
            .SelectMany(r => r.Attendees)
            .ToListAsync(cancellationToken);
        var filtered = string.IsNullOrWhiteSpace(search)
            ? attendees
            : attendees.Where(a => a.Name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        return Result<IReadOnlyList<TicketDto>>.Ok(filtered.OrderBy(a => a.Name).Take(100).Select(a => new TicketDto(a.Id, a.Name, string.Empty, a.CheckedInAt)).ToList());
    }

    // ---------- Core ----------

    private async Task<Result<(Registration Registration, Event Event)>> PlaceAsync(
        Guid eventId,
        Guid registrantPersonId,
        List<(Guid? PersonId, string Name)> attendees,
        IReadOnlyDictionary<Guid, string>? answers,
        RegistrationSource source,
        string? guestKeyHash,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        await db.LockEventAsync(eventId, cancellationToken);
        var e = await db.Events.AsNoTracking().SingleAsync(x => x.Id == eventId, cancellationToken);
        var now = clock.GetUtcNow();
        if (e.RegistrationClosedReason(now) is { } closed)
        {
            return new Error("events.registration_closed", closed);
        }

        var validated = e.ValidateAnswers(answers);
        var personIds = attendees.Where(a => a.PersonId is not null).Select(a => a.PersonId!.Value).Append(registrantPersonId).Distinct().ToList();
        var alreadyIn = await db.Registrations.AnyAsync(
            r => r.EventId == eventId && r.Status != RegistrationStatus.Cancelled
                && (personIds.Contains(r.RegistrantPersonId) || r.Attendees.Any(a => a.PersonId != null && personIds.Contains(a.PersonId.Value))),
            cancellationToken);
        if (alreadyIn)
        {
            return Error.Conflict("events.already_registered", "You (or someone you're registering) is already booked for this event.");
        }

        var decision = Seating.Decide(e, await reader.ConfirmedSeatsAsync(eventId, cancellationToken), await reader.AnyoneWaitingAsync(eventId, cancellationToken), attendees.Count);
        if (decision.IsFailure)
        {
            return decision.Error!;
        }

        var registration = Registration.Create(e, registrantPersonId, attendees, validated, decision.Value, source, now, guestKeyHash);
        db.Registrations.Add(registration);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (registration, e);
    }

    private async Task<Result> CancelAndPromoteAsync(Guid registrationId, CancellationToken cancellationToken)
    {
        var eventId = await db.Registrations.Where(r => r.Id == registrationId).Select(r => r.EventId).SingleAsync(cancellationToken);
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        await db.LockEventAsync(eventId, cancellationToken);

        var registration = await db.Registrations.SingleAsync(r => r.Id == registrationId, cancellationToken);
        var wasConfirmed = registration.Status == RegistrationStatus.Confirmed;
        var now = clock.GetUtcNow();
        registration.Cancel(now);
        await db.SaveChangesAsync(cancellationToken);

        var e = await db.Events.AsNoTracking().SingleAsync(x => x.Id == eventId, cancellationToken);
        if (wasConfirmed && e.Status == EventStatus.Published && e.StartsAt > now)
        {
            var waitlist = await db.Registrations
                .Where(r => r.EventId == eventId && r.Status == RegistrationStatus.Waitlisted)
                .OrderBy(r => r.CreatedAt)
                .ToListAsync(cancellationToken);
            foreach (var next in Seating.ToPromote(e, await reader.ConfirmedSeatsAsync(eventId, cancellationToken), waitlist))
            {
                next.Promote(now);
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<Registration?> FindGuestAsync(Guid registrationId, string key, CancellationToken cancellationToken)
    {
        var r = await db.Registrations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == registrationId, cancellationToken);
        if (r?.GuestKeyHash is null || string.IsNullOrEmpty(key))
        {
            return null;
        }

        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(r.GuestKeyHash), Encoding.ASCII.GetBytes(hasher.Hash(KeyPurpose, key))) ? r : null;
    }

    private async Task<List<AttendeeRowDto>> RowsAsync(Event e, CancellationToken cancellationToken)
    {
        var registrations = await db.Registrations.AsNoTracking().Where(r => r.EventId == e.Id).OrderBy(r => r.CreatedAt).ToListAsync(cancellationToken);
        var registrants = await people.GetManyAsync(registrations.Select(r => r.RegistrantPersonId).Distinct().ToList(), cancellationToken);
        var labels = e.Questions.ToDictionary(q => q.Id, q => q.Label);
        return registrations.SelectMany(r => r.Attendees.Select(a => new AttendeeRowDto(
                r.Id,
                a.Id,
                a.Name,
                a.PersonId,
                registrants.GetValueOrDefault(r.RegistrantPersonId)?.DisplayName ?? "Unknown",
                registrants.GetValueOrDefault(r.RegistrantPersonId)?.Email,
                r.Status,
                r.Source,
                r.CreatedAt,
                a.CheckedInAt,
                r.Answers.Where(kv => labels.ContainsKey(kv.Key)).ToDictionary(kv => labels[kv.Key], kv => kv.Value))))
            .ToList();
    }

    /// <summary>Quotes a CSV cell, and defuses spreadsheet formulas (=, +, -, @) that could run when opened.</summary>
    private static string Cell(string value)
    {
        var safe = value.Length > 0 && "=+-@".Contains(value[0], StringComparison.Ordinal) ? $"'{value}" : value;
        return $"\"{safe.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
