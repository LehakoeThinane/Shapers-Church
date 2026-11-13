using Shapers.People.Application;

namespace Shapers.IntegrationTests;

public sealed class PeopleImportTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task An_import_adds_people_as_they_are_and_a_second_upload_adds_no_copies()
    {
        var admin = await api.SignInAdminAsync();
        ImportPersonRow[] rows =
        [
            new(2, "Lindiwe", "Import", new DateOnly(1994, 1, 15), ["lindiwe.import@example.com"], ["076 144 3247", "076 144 3247"]),
            // Two people sharing an email and phone are still two people.
            new(3, "Iris", "Shared", null, ["shared.import@example.com"], ["081 038 5549"]),
            new(4, "Kayla", "Shared", null, ["shared.import@example.com"], ["081 038 5549"]),
            new(5, "", "Nameless", null, [], []),
            new(6, "Bad", "Contact", null, ["not-an-email"], []),
        ];

        var first = await (await admin.PostJsonAsync("/api/admin/people/import", new ImportPeopleRequest(null, null, rows))).ReadAsync<ImportPeopleResult>();
        Assert.Equal(4, first.Added);
        Assert.Equal(0, first.AlreadyThere);
        Assert.Equal(5, Assert.Single(first.Skipped).RowNumber);
        Assert.Equal(6, Assert.Single(first.Warnings).RowNumber);

        var again = await (await admin.PostJsonAsync("/api/admin/people/import", new ImportPeopleRequest(null, null, rows))).ReadAsync<ImportPeopleResult>();
        Assert.Equal(1, again.Added); // "Bad Contact" has no contact to match on, so it can't be recognised.
        Assert.Equal(3, again.AlreadyThere);
    }
}
