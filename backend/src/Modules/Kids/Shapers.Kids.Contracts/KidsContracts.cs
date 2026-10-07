namespace Shapers.Kids.Contracts;

public static class KidsPermissions
{
    /// <summary>Run kids check-in: see who is in each class today, check children in at the desk and hand them back.</summary>
    public const string CheckIn = "kids.checkin";

    /// <summary>Read children's care notes (allergies, medical needs). Health information about children: sensitive and audited.</summary>
    public const string CareView = "kids.care.view";

    /// <summary>Set up the classes and their age ranges.</summary>
    public const string Manage = "kids.manage";
}
