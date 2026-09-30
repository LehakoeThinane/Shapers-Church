namespace Shapers.Communications.Contracts;

public static class CommunicationsPermissions
{
    /// <summary>See the delivery log: who was sent what, and whether it arrived.</summary>
    public const string DeliveriesView = "communications.deliveries.view";

    /// <summary>Write announcements and send them to a ministry; wider audiences need approval.</summary>
    public const string AnnouncementsSend = "communications.announcements.send";

    /// <summary>Approve announcements for a whole campus or the whole church.</summary>
    public const string AnnouncementsApprove = "communications.announcements.approve";
}
