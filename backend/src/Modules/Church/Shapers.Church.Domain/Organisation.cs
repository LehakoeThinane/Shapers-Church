using Shapers.SharedKernel;

namespace Shapers.Church.Domain;

/// <summary>
/// The church itself and the root of the scope tree. Shapers is single-tenant, so there is exactly one.
/// </summary>
public sealed class Organisation : AggregateRoot<Guid>
{
    private Organisation()
    {
    }

    public string Name { get; private set; } = null!;

    public string Slug { get; private set; } = null!;

    public string? LegalName { get; private set; }

    /// <summary>Public Benefit Organisation reference from SARS, if registered.</summary>
    public string? PboNumber { get; private set; }

    /// <summary>When true, Giving issues Section 18A certificates rather than plain receipts.</summary>
    public bool IsSection18AApproved { get; private set; }

    public string TimeZone { get; private set; } = null!;

    public string Currency { get; private set; } = null!;

    public string? ContactEmail { get; private set; }

    public string? Website { get; private set; }

    public string Scope { get; private set; } = null!;

    public static Organisation Create(string name, string slug, string timeZone = "Africa/Johannesburg", string currency = "ZAR")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var scope = ScopePath.Organisation(slug);
        return new Organisation
        {
            Id = Guid.CreateVersion7(),
            Name = name.Trim(),
            Slug = scope.Value,
            TimeZone = timeZone,
            Currency = currency,
            Scope = scope.Value,
        };
    }

    public void UpdateDetails(string name, string? legalName, string? contactEmail, string? website)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        LegalName = legalName?.Trim();
        ContactEmail = contactEmail?.Trim();
        Website = website?.Trim();
    }

    public void SetTaxStatus(string? pboNumber, bool isSection18AApproved)
    {
        if (isSection18AApproved && string.IsNullOrWhiteSpace(pboNumber))
        {
            throw new InvalidOperationException("Section 18A approval requires a PBO number.");
        }

        PboNumber = pboNumber?.Trim();
        IsSection18AApproved = isSection18AApproved;
    }
}
