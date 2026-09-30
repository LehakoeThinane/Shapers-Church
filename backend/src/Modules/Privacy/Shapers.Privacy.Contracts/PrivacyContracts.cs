namespace Shapers.Privacy.Contracts;

public static class PrivacyPermissions
{
    /// <summary>The Information Officer's queue: correction and deletion requests. Deciding a deletion erases the person.</summary>
    public const string RequestsManage = "privacy.requests.manage";
}

/// <summary>A person was erased across the platform, on request or by retention.</summary>
public sealed record PersonErasedIntegrationEvent(Guid PersonId, string Reason) : IntegrationEvent;
