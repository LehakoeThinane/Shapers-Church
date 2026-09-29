namespace Shapers.People.Domain;

/// <summary>The POPIA ground for processing (section 11, and section 27 for special personal information).</summary>
public enum LawfulBasis
{
    Consent,
    LegitimateInterest,
    LegalObligation,
    Contract,
}

public enum ConsentSource
{
    MobileApp,
    Website,
    AdminPortal,
    PaperForm,
    Import,
}

/// <summary>Well-known purposes. Stored as strings so new purposes don't need a migration.</summary>
public static class ConsentPurposes
{
    /// <summary>Keeping a church record at all. Membership reveals religious belief (special personal information).</summary>
    public const string ChurchRecord = "processing.church_record";
    public const string EmailCommunication = "communications.email";
    public const string SmsCommunication = "communications.sms";
    public const string WhatsAppCommunication = "communications.whatsapp";
    public const string PushNotifications = "communications.push";
    public const string PhotosAndVideo = "media.photos";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        ChurchRecord, EmailCommunication, SmsCommunication, WhatsAppCommunication, PushNotifications, PhotosAndVideo,
    };
}

/// <summary>
/// Evidence of a consent decision. Append-only: withdrawing consent adds a new record with
/// <see cref="Granted"/> = false. The current position for a purpose is the latest record.
/// </summary>
public sealed class ConsentRecord
{
    private ConsentRecord()
    {
    }

    public Guid Id { get; private set; }

    public Guid PersonId { get; private set; }

    public string Purpose { get; private set; } = null!;

    public bool Granted { get; private set; }

    public LawfulBasis LawfulBasis { get; private set; }

    /// <summary>The version of the privacy notice the person saw, e.g. "2026-09".</summary>
    public string PolicyVersion { get; private set; } = null!;

    public ConsentSource Source { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    /// <summary>Who captured it. Null when the person recorded it themselves.</summary>
    public Guid? RecordedByUserId { get; private set; }

    public static ConsentRecord Record(
        Guid personId,
        string purpose,
        bool granted,
        LawfulBasis lawfulBasis,
        string policyVersion,
        ConsentSource source,
        DateTimeOffset now,
        Guid? recordedBy)
    {
        if (!ConsentPurposes.All.Contains(purpose))
        {
            throw new DomainRuleException("people.consent_purpose_unknown", $"Unknown consent purpose '{purpose}'.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(policyVersion);
        return new ConsentRecord
        {
            Id = Guid.CreateVersion7(),
            PersonId = personId,
            Purpose = purpose,
            Granted = granted,
            LawfulBasis = lawfulBasis,
            PolicyVersion = policyVersion.Trim(),
            Source = source,
            RecordedAt = now,
            RecordedByUserId = recordedBy,
        };
    }

    /// <summary>The latest decision per purpose.</summary>
    public static IReadOnlyDictionary<string, ConsentRecord> Current(IEnumerable<ConsentRecord> history) =>
        history
            .GroupBy(r => r.Purpose)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.RecordedAt).ThenBy(r => r.Id).Last());
}
