namespace Shapers.Events.Contracts;

public static class EventsPermissions
{
    public const string Edit = "events.edit";
    public const string Publish = "events.publish";

    /// <summary>Attendance lists reveal who takes part in church life: sensitive under POPIA.</summary>
    public const string RegistrationsView = "events.registrations.view";
    public const string RegistrationsManage = "events.registrations.manage";

    /// <summary>Door volunteers: scan tickets and find names, nothing more.</summary>
    public const string CheckIn = "events.checkin";
}

public sealed record EventRegisteredIntegrationEvent(Guid RegistrationId, Guid ChurchEventId, Guid PersonId, string Status) : IntegrationEvent;

public sealed record WaitlistPromotedIntegrationEvent(Guid RegistrationId, Guid ChurchEventId, Guid PersonId) : IntegrationEvent;

public sealed record EventCancelledIntegrationEvent(Guid ChurchEventId, string Title) : IntegrationEvent;
