using Microsoft.EntityFrameworkCore;
using Shapers.Church.Contracts;
using Shapers.People.Contracts;
using Shapers.People.Domain;
using Shapers.Platform.Text;

namespace Shapers.People.Application;

/// <summary>Creates church records for guests (connect cards, event registrations) with consent and duplicate review.</summary>
public sealed class GuestRecords(IPeopleDb db, IChurchDirectory church, DuplicateDetector duplicates, TimeProvider clock) : IGuestRecords
{
    public async Task<Result<Guid>> CreateAsync(GuestDetails guest, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(guest.FirstName) || string.IsNullOrWhiteSpace(guest.LastName))
        {
            return new Error("people.name_required", "Please tell us your first and last name.");
        }

        string? mobile = null;
        if (!string.IsNullOrWhiteSpace(guest.Mobile) && !ContactNormaliser.TryNormalisePhone(guest.Mobile, out mobile))
        {
            return new Error("people.mobile_invalid", "That mobile number doesn't look right.");
        }

        string? email = null;
        if (!string.IsNullOrWhiteSpace(guest.Email) && !ContactNormaliser.TryNormaliseEmail(guest.Email, out email))
        {
            return new Error("people.email_invalid", "That email address doesn't look right.");
        }

        if (mobile is null && email is null)
        {
            return new Error("people.contact_required", "Give us a mobile number or email so we can get back to you.");
        }

        if (!guest.ConsentToKeepDetails)
        {
            return new Error("people.consent_required", "We need your permission to keep your details so the church can contact you.");
        }

        var now = clock.GetUtcNow();
        var campuses = await church.GetCampusesAsync(cancellationToken);
        var campus = campuses.FirstOrDefault(c => c.IsPrimary) ?? (campuses.Count > 0 ? campuses[0] : null);
        var scope = ScopePath.Parse(campus?.Scope ?? (await church.GetRootScopeAsync(cancellationToken)).Path);
        var status = await db.MembershipStatuses.SingleAsync(s => s.IsDefault, cancellationToken);
        var source = guest.Origin == GuestOrigin.EventRegistration ? PersonSource.EventRegistration : PersonSource.VisitorCard;

        var person = Person.Create(scope, guest.FirstName, guest.LastName, status, source, now);
        if (mobile is not null)
        {
            person.AddContact(ContactType.Mobile, mobile, isPrimary: true, isVerified: false, now);
        }

        if (email is not null)
        {
            person.AddContact(ContactType.Email, email, isPrimary: true, isVerified: guest.EmailVerified, now);
        }

        db.Persons.Add(person);
        db.ConsentRecords.Add(ConsentRecord.Record(
            person.Id,
            ConsentPurposes.ChurchRecord,
            granted: true,
            LawfulBasis.Consent,
            string.IsNullOrWhiteSpace(guest.PolicyVersion) ? "2026-09" : guest.PolicyVersion,
            guest.FromWebsite ? ConsentSource.Website : ConsentSource.MobileApp,
            now,
            recordedBy: null));
        await db.SaveChangesAsync(cancellationToken);
        await duplicates.DetectAsync(person, cancellationToken);
        return person.Id;
    }
}
