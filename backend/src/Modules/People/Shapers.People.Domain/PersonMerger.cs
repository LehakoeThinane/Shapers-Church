namespace Shapers.People.Domain;

/// <summary>Audit trail of a merge, including a snapshot of the duplicate as it was, so it can be undone by hand.</summary>
public sealed class PersonMerge
{
    private PersonMerge()
    {
    }

    public Guid Id { get; private set; }

    public Guid SurvivorId { get; private set; }

    public Guid MergedId { get; private set; }

    public Guid? MergedByUserId { get; private set; }

    public DateTimeOffset MergedAt { get; private set; }

    public string Snapshot { get; private set; } = null!;

    internal static PersonMerge Create(Guid survivorId, Guid mergedId, Guid? by, DateTimeOffset now, string snapshot) => new()
    {
        Id = Guid.CreateVersion7(),
        SurvivorId = survivorId,
        MergedId = mergedId,
        MergedByUserId = by,
        MergedAt = now,
        Snapshot = snapshot,
    };
}

/// <summary>
/// Folds a duplicate record into a survivor. The duplicate stays as a tombstone pointing at the survivor,
/// households are re-pointed, and a PersonMerged event tells other modules to re-point their references.
/// </summary>
public static class PersonMerger
{
    public static PersonMerge Merge(
        Person survivor,
        Person duplicate,
        IEnumerable<Household> duplicateHouseholds,
        string snapshot,
        Guid? mergedBy,
        DateTimeOffset now)
    {
        if (survivor.Id == duplicate.Id)
        {
            throw new DomainRuleException("people.merge_self", "A record cannot be merged into itself.");
        }

        if (survivor.Status == PersonStatus.Merged)
        {
            throw new DomainRuleException("people.merge_into_merged", "The surviving record has itself been merged. Merge into its survivor instead.");
        }

        if (duplicate.Status == PersonStatus.Merged)
        {
            throw new DomainRuleException("people.already_merged", "This record has already been merged.");
        }

        survivor.Absorb(duplicate, now);
        foreach (var household in duplicateHouseholds)
        {
            household.ReplacePerson(duplicate.Id, survivor.Id);
        }

        duplicate.BecomeMergedInto(survivor, now);
        return PersonMerge.Create(survivor.Id, duplicate.Id, mergedBy, now, snapshot);
    }
}
