namespace Shapers.Giving.Contracts;

public static class GivingPermissions
{
    /// <summary>See gifts, totals and givers' statements. Who gives, and how much, is sensitive: reads are audited.</summary>
    public const string View = "giving.view";

    /// <summary>Record EFT and cash gifts, and set up the funds.</summary>
    public const string Manage = "giving.manage";
}
