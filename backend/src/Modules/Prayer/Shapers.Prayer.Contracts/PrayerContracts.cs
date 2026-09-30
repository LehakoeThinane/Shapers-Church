namespace Shapers.Prayer.Contracts;

public static class PrayerPermissions
{
    /// <summary>See every request, including pastors-only ones. Reads are audited.</summary>
    public const string RequestsView = "prayer.requests.view";

    /// <summary>Review requests for the wall and take them down.</summary>
    public const string RequestsModerate = "prayer.requests.moderate";
}

/// <summary>A new request. Carries no text: notifications say only that someone asked for prayer.</summary>
public sealed record PrayerRequestSubmittedIntegrationEvent(Guid RequestId, Guid PersonId, string Scope, bool NeedsReview) : IntegrationEvent;

public sealed record PrayerRequestApprovedIntegrationEvent(Guid RequestId, Guid PersonId) : IntegrationEvent;
