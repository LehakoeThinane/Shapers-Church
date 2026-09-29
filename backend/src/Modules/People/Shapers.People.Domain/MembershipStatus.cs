namespace Shapers.People.Domain;

/// <summary>
/// Fixed stages of the church journey. Status names are configurable per church, but every status maps
/// to a stage so analytics can report the journey consistently (in aggregate, never per person).
/// </summary>
public enum JourneyStage
{
    Visitor = 10,
    Regular = 20,
    GrowthTrack = 30,
    Member = 40,
    Inactive = 90,
}

public sealed class MembershipStatus
{
    private MembershipStatus()
    {
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = null!;

    public JourneyStage Stage { get; private set; }

    public int SortOrder { get; private set; }

    /// <summary>The status given to new records when none is chosen.</summary>
    public bool IsDefault { get; private set; }

    public bool IsActive { get; private set; }

    public static MembershipStatus Create(string name, JourneyStage stage, int sortOrder, bool isDefault = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new MembershipStatus
        {
            Id = Guid.CreateVersion7(),
            Name = name.Trim(),
            Stage = stage,
            SortOrder = sortOrder,
            IsDefault = isDefault,
            IsActive = true,
        };
    }

    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
    }

    public void Retire() => IsActive = false;

    /// <summary>The statuses a new church starts with. Staff can rename them.</summary>
    public static IEnumerable<MembershipStatus> Defaults() =>
    [
        Create("Visitor", JourneyStage.Visitor, 10, isDefault: true),
        Create("Regular attender", JourneyStage.Regular, 20),
        Create("In Growth Track", JourneyStage.GrowthTrack, 30),
        Create("Member", JourneyStage.Member, 40),
        Create("Inactive", JourneyStage.Inactive, 90),
    ];
}
