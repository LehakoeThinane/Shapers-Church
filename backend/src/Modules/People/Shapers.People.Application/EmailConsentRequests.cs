using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shapers.People.Contracts;
using Shapers.People.Domain;
using Shapers.Platform.Auditing;
using Shapers.Platform.Authorization;
using Shapers.Platform.Email;
using Shapers.Platform.Security;
using Shapers.Platform.Web;

namespace Shapers.People.Application;

public sealed class EmailConsentOptions
{
    /// <summary>The API's public address, for the answer link. Defaults to Communications:PublicApiUrl.</summary>
    public string? PublicApiUrl { get; set; }

    public string PrivacyPolicyUrl { get; set; } = "https://shaperschurch.com/privacy";
}

/// <summary>Who can be asked: on our records with an email address, never asked before, and no decision recorded either way.</summary>
public sealed record EmailConsentSummary(int CanBeAsked, int AlreadyAgreed, int SaidNo, int AlreadyAsked, int NoEmail);

public sealed record EmailConsentSendResult(int Sent, int Failed);

/// <summary>
/// Asks people already on our records, once, whether they'd like church emails (POPIA s69(2)). Their answer is
/// recorded as their consent, so from then on announcements reach them by email, or never ask again.
/// </summary>
public sealed partial class EmailConsentRequests(
    IPeopleDb db,
    IAuthorizer authorizer,
    ICurrentUser currentUser,
    IEmailSender email,
    IKeyedHasher hasher,
    IAuditLog audit,
    IOptions<EmailConsentOptions> options,
    TimeProvider clock,
    ILogger<EmailConsentRequests> logger)
{
    public const string AnswerPath = "/api/email-consent";
    private const string Purpose = ConsentPurposes.EmailCommunication;
    private const string SignaturePurpose = "people.email_consent";

    private static readonly Error InvalidLink = new("people.consent_link_invalid",
        "This link isn't valid. Please email info@shaperschurch.com and we'll update your choice.");

    public async Task<EmailConsentSummary> SummaryAsync(CancellationToken cancellationToken)
    {
        var people = await PeopleInScopeAsync(cancellationToken);
        return new EmailConsentSummary(
            people.Count(p => p.CanBeAsked),
            people.Count(p => p.Decision == true),
            people.Count(p => p.Decision == false),
            people.Count(p => p.Decision is null && p.Asked),
            people.Count(p => p.Decision is null && !p.Asked && p.Email is null));
    }

    /// <summary>Emails everyone who can be asked. Each person is marked as asked before their email goes, so a retry never asks twice.</summary>
    public async Task<EmailConsentSendResult> SendAsync(CancellationToken cancellationToken)
    {
        var people = (await PeopleInScopeAsync(cancellationToken)).Where(p => p.CanBeAsked).ToList();
        var now = clock.GetUtcNow();
        var sent = 0;
        var failed = 0;
        foreach (var person in people)
        {
            db.ConsentRequests.Add(ConsentRequest.Sent(person.Id, Purpose, now, currentUser.UserId));
            await db.SaveChangesAsync(cancellationToken);
            try
            {
                await email.SendAsync(Message(person.Id, person.FirstName, person.Email!), cancellationToken);
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogSendFailed(person.Id, ex.Message);
                failed++;
            }
        }

        var scopes = await authorizer.ScopesForAsync(PeoplePermissions.ProfilesEdit, cancellationToken);
        if (scopes.Count > 0)
        {
            await audit.RecordAsync(
                new AuditRecord("people.email_consent.requested", PeopleAudit.EntityPerson, $"{sent} people", scopes[0]),
                cancellationToken);
        }
        return new EmailConsentSendResult(sent, failed);
    }

    /// <summary>The person a link belongs to, for the answer page.</summary>
    public async Task<Result<string>> FindAsync(string? personId, string? signature, CancellationToken cancellationToken)
    {
        var person = await FromLinkAsync(personId, signature, cancellationToken);
        return person is null ? InvalidLink : person.FirstName;
    }

    public async Task<Result> AnswerAsync(string? personId, string? signature, bool agree, CancellationToken cancellationToken)
    {
        var person = await FromLinkAsync(personId, signature, cancellationToken);
        if (person is null)
        {
            return InvalidLink;
        }

        db.ConsentRecords.Add(ConsentRecord.Record(
            person.Id, Purpose, agree, LawfulBasis.Consent, FamilyRecords.PolicyVersion, ConsentSource.EmailRequest, clock.GetUtcNow(), recordedBy: null));
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<Person?> FromLinkAsync(string? personId, string? signature, CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(personId, "N", out var id) ||
            !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Sign(id)), Encoding.ASCII.GetBytes(signature ?? string.Empty)))
        {
            return null;
        }

        return await db.Persons.SingleOrDefaultAsync(p => p.Id == id && p.Status == PersonStatus.Active, cancellationToken);
    }

    private async Task<List<Candidate>> PeopleInScopeAsync(CancellationToken cancellationToken)
    {
        var scopes = await authorizer.ScopesForAsync(PeoplePermissions.ProfilesEdit, cancellationToken);
        var people = await db.Persons.AsNoTracking()
            .WithinScopes(p => p.Scope, scopes)
            .Where(p => p.Status == PersonStatus.Active)
            .Select(p => new
            {
                p.Id,
                p.FirstName,
                Email = p.Contacts.Where(c => c.Type == ContactType.Email).OrderByDescending(c => c.IsPrimary).Select(c => c.Value).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var ids = people.Select(p => p.Id).ToList();
        var decisions = (await db.ConsentRecords.AsNoTracking()
                .Where(c => c.Purpose == Purpose && ids.Contains(c.PersonId))
                .ToListAsync(cancellationToken))
            .GroupBy(c => c.PersonId)
            .ToDictionary(g => g.Key, g => ConsentRecord.Current(g)[Purpose].Granted);
        var asked = (await db.ConsentRequests.AsNoTracking()
                .Where(r => r.Purpose == Purpose && ids.Contains(r.PersonId))
                .Select(r => r.PersonId)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        return people
            .Select(p => new Candidate(p.Id, p.FirstName, p.Email, decisions.TryGetValue(p.Id, out var d) ? d : null, asked.Contains(p.Id)))
            .ToList();
    }

    private EmailMessage Message(Guid personId, string firstName, string to)
    {
        var link = $"{(options.Value.PublicApiUrl ?? "http://localhost:5080").TrimEnd('/')}{AnswerPath}?p={personId:N}&s={Sign(personId)}";
        const string subject = "Would you like to hear from Shapers Church?";
        string[] paragraphs =
        [
            $"Hi {firstName},",
            "You're on Shapers Church's records, and we'd love to keep you in the loop by email: what's happening at church, upcoming events, and the occasional encouragement.",
            "We'll only email you if you say yes. If you'd rather not, choose no or simply ignore this email, and we won't ask again.",
            "If you say yes, every email will have a link to unsubscribe, and you can change your mind at any time.",
            $"Shapers Church, info@shaperschurch.com. How we look after your information: {options.Value.PrivacyPolicyUrl}",
        ];
        return new EmailMessage(
            to,
            subject,
            string.Join("\n\n", paragraphs.Append($"Yes or no: {link}")),
            EmailLayout.Html(subject, paragraphs, "Choose yes or no", link));
    }

    private string Sign(Guid personId) => hasher.Hash(SignaturePurpose, personId.ToString("N"))[..32];

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't send the email consent request to person {PersonId}: {Reason}")]
    private partial void LogSendFailed(Guid personId, string reason);

    private sealed record Candidate(Guid Id, string FirstName, string? Email, bool? Decision, bool Asked)
    {
        public bool CanBeAsked => Decision is null && !Asked && Email is not null;
    }
}
