namespace Shapers.Groups.Contracts;

public static class GroupsPermissions
{
    /// <summary>Create and close cells, and choose their leaders and members.</summary>
    public const string CellsManage = "groups.cells.manage";

    /// <summary>Read cell reports and leaders' teaching materials. Reports can hold special personal information, so reads are audited.</summary>
    public const string ReportsView = "groups.reports.view";
}

/// <summary>A visitor at a cell meeting who agreed to be contacted by the church.</summary>
public sealed record CellVisitor(string FirstName, string? LastName, string? Mobile, string? Email);

/// <summary>
/// A submitted cell report recorded visitors who agreed to be contacted. People files each one as a connect card so
/// the follow-up team sees them. Visitors who didn't agree never leave the report.
/// </summary>
public sealed record CellVisitorsRecordedIntegrationEvent(Guid ReportId, Guid CellId, string CellName, DateOnly MeetingDate, IReadOnlyList<CellVisitor> Visitors) : IntegrationEvent;

/// <summary>A leader flagged a follow-up as urgent. Carries no names or details: pastors open the report to see them.</summary>
public sealed record CellFollowUpFlaggedIntegrationEvent(Guid ReportId, Guid CellId, string CellName, string Scope) : IntegrationEvent;
