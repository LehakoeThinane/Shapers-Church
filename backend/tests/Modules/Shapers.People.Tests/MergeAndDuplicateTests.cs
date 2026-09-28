using Shapers.People.Domain;

namespace Shapers.People.Tests;

public sealed class PersonMergerTests
{
    [Fact]
    public void Merge_leaves_a_tombstone_moves_contacts_and_raises_an_event()
    {
        var survivor = Build.Person(mobile: "+27821234567");
        var duplicate = Build.Person(email: "thandi@example.com", dob: new DateOnly(1990, 5, 1));

        var merge = PersonMerger.Merge(survivor, duplicate, [], "{}", Guid.NewGuid(), Build.Now);

        Assert.Equal(PersonStatus.Merged, duplicate.Status);
        Assert.Equal(survivor.Id, duplicate.MergedIntoId);
        Assert.Equal(2, survivor.Contacts.Count);
        Assert.Equal(new DateOnly(1990, 5, 1), survivor.DateOfBirth);
        Assert.Equal(survivor.Id, merge.SurvivorId);
        var merged = Assert.IsType<PersonMerged>(Assert.Single(duplicate.DomainEvents));
        Assert.Equal(survivor.Id, merged.SurvivorId);
    }

    [Fact]
    public void Survivor_keeps_its_own_values_where_it_has_them()
    {
        var survivor = Build.Person(dob: new DateOnly(1990, 5, 1));
        var duplicate = Build.Person(dob: new DateOnly(1991, 1, 1));

        PersonMerger.Merge(survivor, duplicate, [], "{}", null, Build.Now);

        Assert.Equal(new DateOnly(1990, 5, 1), survivor.DateOfBirth);
    }

    [Fact]
    public void Households_are_repointed_without_duplicating_the_survivor()
    {
        var survivor = Build.Person();
        var duplicate = Build.Person();
        var shared = Household.Create("Mokoena", Build.Rivonia, Build.Now);
        shared.AddMember(survivor.Id, HouseholdRole.Adult);
        shared.AddMember(duplicate.Id, HouseholdRole.Adult);
        var other = Household.Create("Gran's house", Build.Rivonia, Build.Now);
        other.AddMember(duplicate.Id, HouseholdRole.Adult);

        PersonMerger.Merge(survivor, duplicate, [shared, other], "{}", null, Build.Now);

        Assert.Equal([survivor.Id], shared.Members.Select(m => m.PersonId));
        Assert.Equal([survivor.Id], other.Members.Select(m => m.PersonId));
        Assert.Equal(survivor.Id, other.PrimaryContactId);
    }

    [Fact]
    public void Merged_records_are_frozen()
    {
        var survivor = Build.Person();
        var duplicate = Build.Person();
        PersonMerger.Merge(survivor, duplicate, [], "{}", null, Build.Now);

        Assert.Throws<DomainRuleException>(() => duplicate.Rename("A", "B", null, Build.Now));
        Assert.Throws<DomainRuleException>(() => PersonMerger.Merge(survivor, duplicate, [], "{}", null, Build.Now));
        Assert.Throws<DomainRuleException>(() => PersonMerger.Merge(duplicate, Build.Person(), [], "{}", null, Build.Now));
    }

    [Fact]
    public void Cannot_merge_into_self()
    {
        var person = Build.Person();

        Assert.Throws<DomainRuleException>(() => PersonMerger.Merge(person, person, [], "{}", null, Build.Now));
    }
}

public sealed class DuplicateMatcherTests
{
    [Fact]
    public void Same_phone_and_name_is_a_likely_duplicate()
    {
        var match = DuplicateMatcher.Compare(
            Build.Person("Thandi", "Mokoena", mobile: "+27821234567"),
            Build.Person("thandi", "MOKOENA", mobile: "+27821234567"));

        Assert.True(match.IsLikely);
        Assert.Contains("same phone number", match.Reasons);
        Assert.Contains("same name", match.Reasons);
    }

    [Fact]
    public void Family_sharing_a_phone_is_not_a_duplicate()
    {
        var mum = Build.Person("Thandi", "Mokoena", dob: new DateOnly(1985, 3, 2), mobile: "+27821234567");
        var son = Build.Person("Lerato", "Mokoena", dob: new DateOnly(2014, 6, 9), mobile: "+27821234567");

        Assert.False(DuplicateMatcher.Compare(mum, son).IsLikely);
    }

    [Fact]
    public void Preferred_name_counts_as_the_same_first_name()
    {
        var formal = Build.Person("Nomathemba", "Dlamini", email: "themba@example.com");
        formal.Rename("Nomathemba", "Dlamini", "Themba", Build.Now);
        var casual = Build.Person("Themba", "Dlamini", email: "themba@example.com");

        Assert.True(DuplicateMatcher.Compare(formal, casual).IsLikely);
    }

    [Fact]
    public void Different_birthdays_outweigh_a_shared_email()
    {
        var a = Build.Person("Sipho", "Nkosi", dob: new DateOnly(1980, 1, 1), email: "nkosi.family@example.com");
        var b = Build.Person("Sipho", "Nkosi", dob: new DateOnly(2010, 1, 1), email: "nkosi.family@example.com");

        Assert.False(DuplicateMatcher.Compare(a, b).IsLikely);
    }

    [Theory]
    [InlineData("Thánde-Mokoena", "thande mokoena")]
    [InlineData("O'Neill", "oneill")]
    public void Names_are_compared_without_accents_punctuation_or_case(string a, string b)
    {
        Assert.Equal(DuplicateMatcher.NormaliseName(a), DuplicateMatcher.NormaliseName(b));
    }
}
