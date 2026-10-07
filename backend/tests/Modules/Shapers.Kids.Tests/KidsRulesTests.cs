using Shapers.Kids.Domain;

namespace Shapers.Kids.Tests;

public sealed class KidsRulesTests
{
    private static readonly ScopePath Church = ScopePath.Parse("shapers");
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 7, 30, 0, TimeSpan.Zero);
    private static readonly DateOnly Sunday = new(2026, 10, 11);

    [Theory]
    [InlineData("2023-10-11", 3)] // third birthday today
    [InlineData("2023-10-12", 2)] // third birthday tomorrow
    [InlineData("2020-02-29", 6)]
    public void Age_counts_whole_years_on_the_day(string dob, int expected) =>
        Assert.Equal(expected, KidsClass.AgeOn(DateOnly.Parse(dob), Sunday));

    [Fact]
    public void A_child_goes_to_the_narrowest_class_that_fits_their_age()
    {
        var primary = KidsClass.Create("Primary", 6, 9, Church, Now);
        var all = KidsClass.Create("Everyone", 0, 12, Church, Now);
        var toddlers = KidsClass.Create("Little ones", 0, 2, Church, Now);
        var classes = new[] { all, primary, toddlers };

        Assert.Same(primary, KidsClass.For(classes, 7));
        Assert.Same(toddlers, KidsClass.For(classes, 1));
        Assert.Same(all, KidsClass.For(classes, 4));
        Assert.Null(KidsClass.For(classes, 15));

        primary.Archive();
        Assert.Same(all, KidsClass.For(classes, 7));
    }

    [Theory]
    [InlineData("", 0, 2)]
    [InlineData("Pre-school", 5, 3)]
    [InlineData("Teens", 13, 18)]
    [InlineData("Babies", -1, 1)]
    public void Class_names_and_ages_must_make_sense(string name, int from, int to) =>
        Assert.Throws<DomainRuleException>(() => KidsClass.Create(name, from, to, Church, Now));

    [Fact]
    public void Pickup_codes_avoid_characters_that_are_easy_to_misread()
    {
        for (var i = 0; i < 500; i++)
        {
            var code = CheckIn.NewCode();
            Assert.True(CheckIn.IsValidCode(code), code);
            Assert.DoesNotContain(code, c => "01OIL".Contains(c));
        }
    }

    [Fact]
    public void A_child_is_only_handed_back_with_the_matching_code_and_only_once()
    {
        var checkIn = CheckIn.Create(Guid.NewGuid(), Guid.NewGuid(), KidsClass.Create("Primary", 6, 9, Church, Now), Church, Sunday, "K7MX", CheckInMethod.App, null, Now);
        var leader = Guid.NewGuid();

        var wrong = Assert.Throws<DomainRuleException>(() => checkIn.Collect("K7MY", leader, Now.AddHours(2)));
        Assert.Equal("kids.code_mismatch", wrong.Code);
        Assert.False(checkIn.IsCollected);

        checkIn.Collect(" k7mx ", leader, Now.AddHours(2));
        Assert.True(checkIn.IsCollected);
        Assert.Equal(leader, checkIn.CollectedByUserId);

        Assert.Equal("kids.already_collected", Assert.Throws<DomainRuleException>(() => checkIn.Collect("K7MX", leader, Now.AddHours(3))).Code);
    }

    [Fact]
    public void Care_notes_are_trimmed_kept_short_and_empty_when_blank()
    {
        var note = CareNote.For(Guid.NewGuid());
        note.Update("  Peanuts (EpiPen in bag) ", " ", null, Now);
        Assert.Equal("Peanuts (EpiPen in bag)", note.Allergies);
        Assert.Null(note.Medical);
        Assert.False(note.IsEmpty);

        note.Update(null, null, "", Now);
        Assert.True(note.IsEmpty);

        Assert.Throws<DomainRuleException>(() => note.Update(new string('x', CareNote.MaxLength + 1), null, null, Now));
    }
}
