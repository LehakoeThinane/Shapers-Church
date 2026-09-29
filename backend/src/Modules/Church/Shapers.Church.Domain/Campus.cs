using Shapers.SharedKernel;

namespace Shapers.Church.Domain;

public enum CampusStatus
{
    Planned,
    Active,
    Closed,
}

public sealed record Address(
    string Line1,
    string? Line2,
    string? Suburb,
    string City,
    string Province,
    string PostalCode,
    string CountryCode = "ZA");

public sealed record CampusCreated(Guid CampusId, string Name, string Scope) : IDomainEvent;

public sealed class Campus : AggregateRoot<Guid>
{
    private Campus()
    {
    }

    public Guid OrganisationId { get; private set; }

    public string Name { get; private set; } = null!;

    /// <summary>Fixed at creation because it forms part of the scope path. Renaming a campus keeps its slug.</summary>
    public string Slug { get; private set; } = null!;

    public string Scope { get; private set; } = null!;

    public Address? Address { get; private set; }

    public string TimeZone { get; private set; } = null!;

    public CampusStatus Status { get; private set; }

    public bool IsPrimary { get; private set; }

    public static Campus Create(Organisation organisation, string name, string slug, Address? address, bool isPrimary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var scope = ScopePath.Parse(organisation.Scope).Child(ScopeType.Campus, slug);
        var campus = new Campus
        {
            Id = Guid.CreateVersion7(),
            OrganisationId = organisation.Id,
            Name = name.Trim(),
            Slug = ScopePath.Label(slug),
            Scope = scope.Value,
            Address = address,
            TimeZone = organisation.TimeZone,
            Status = CampusStatus.Active,
            IsPrimary = isPrimary,
        };
        campus.Raise(new CampusCreated(campus.Id, campus.Name, campus.Scope));
        return campus;
    }

    public void Update(string name, Address? address, CampusStatus status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        Address = address;
        Status = status;
    }
}
