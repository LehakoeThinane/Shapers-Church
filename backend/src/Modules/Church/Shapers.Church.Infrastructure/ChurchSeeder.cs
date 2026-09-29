using Microsoft.EntityFrameworkCore;
using Shapers.Church.Application;
using Shapers.Church.Domain;

namespace Shapers.Church.Infrastructure;

/// <summary>The church's own details, bound from the "Church" configuration section.</summary>
public sealed class ChurchOptions
{
    public const string SectionName = "Church";

    public string Name { get; set; } = "Shapers Church";

    public string Slug { get; set; } = "shapers";

    public string? ContactEmail { get; set; }

    public string? Website { get; set; }

    public CampusOptions PrimaryCampus { get; set; } = new();

    /// <summary>Development only: example ministries so scoping can be exercised locally.</summary>
    public List<string> DemoMinistries { get; set; } = [];

    public sealed class CampusOptions
    {
        public string Name { get; set; } = "Rivonia";

        public string Slug { get; set; } = "rivonia";

        public AddressOptions? Address { get; set; }
    }

    /// <summary>Configuration-friendly address (settable properties, all optional except the first line and city).</summary>
    public sealed class AddressOptions
    {
        public string Line1 { get; set; } = string.Empty;

        public string? Line2 { get; set; }

        public string? Suburb { get; set; }

        public string City { get; set; } = string.Empty;

        public string Province { get; set; } = string.Empty;

        public string PostalCode { get; set; } = string.Empty;

        public string CountryCode { get; set; } = "ZA";

        public Address ToDomain() => new(Line1, Line2, Suburb, City, Province, PostalCode, CountryCode);
    }
}

internal sealed class ChurchSeeder(ChurchDbContext db)
{
    public async Task SeedAsync(ChurchOptions options, bool includeDemoData, CancellationToken cancellationToken)
    {
        var organisation = await db.Organisations.SingleOrDefaultAsync(cancellationToken);
        if (organisation is null)
        {
            organisation = Organisation.Create(options.Name, options.Slug);
            organisation.UpdateDetails(options.Name, null, options.ContactEmail, options.Website);
            db.Organisations.Add(organisation);
        }

        var campus = await db.Campuses.SingleOrDefaultAsync(c => c.IsPrimary, cancellationToken);
        if (campus is null)
        {
            var c = options.PrimaryCampus;
            campus = Campus.Create(organisation, c.Name, c.Slug, c.Address?.ToDomain(), isPrimary: true);
            db.Campuses.Add(campus);
        }
        else if (campus.Address is null && options.PrimaryCampus.Address is { } address)
        {
            // Fill in an address configured after the campus was first created. Never overwrite staff edits.
            campus.Update(campus.Name, address.ToDomain(), campus.Status);
        }

        if (includeDemoData)
        {
            foreach (var name in options.DemoMinistries)
            {
                var ministry = Ministry.Create(organisation, campus, name, name);
                if (!await db.Ministries.AnyAsync(m => m.Scope == ministry.Scope, cancellationToken))
                {
                    db.Ministries.Add(ministry);
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
