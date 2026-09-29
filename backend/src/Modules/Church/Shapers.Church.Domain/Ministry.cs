using Shapers.SharedKernel;

namespace Shapers.Church.Domain;

public sealed record MinistryCreated(Guid MinistryId, Guid? CampusId, string Name, string Scope) : IDomainEvent;

/// <summary>
/// An area of church life (Kids, Youth, Worship...). A ministry either belongs to one campus
/// or spans the whole church. Either way it is a scope that grants and records can target.
/// </summary>
public sealed class Ministry : AggregateRoot<Guid>
{
    private Ministry()
    {
    }

    public Guid OrganisationId { get; private set; }

    public Guid? CampusId { get; private set; }

    public string Name { get; private set; } = null!;

    public string Slug { get; private set; } = null!;

    public string Scope { get; private set; } = null!;

    public bool IsActive { get; private set; }

    public static Ministry Create(Organisation organisation, Campus? campus, string name, string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var parent = ScopePath.Parse(campus?.Scope ?? organisation.Scope);
        var ministry = new Ministry
        {
            Id = Guid.CreateVersion7(),
            OrganisationId = organisation.Id,
            CampusId = campus?.Id,
            Name = name.Trim(),
            Slug = ScopePath.Label(slug),
            Scope = parent.Child(ScopeType.Ministry, slug).Value,
            IsActive = true,
        };
        ministry.Raise(new MinistryCreated(ministry.Id, ministry.CampusId, ministry.Name, ministry.Scope));
        return ministry;
    }

    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
    }

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;
}
