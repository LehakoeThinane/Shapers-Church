using Shapers.People.Domain;

namespace Shapers.People.Tests;

internal static class Build
{
    public static readonly DateTimeOffset Now = new(2026, 9, 28, 8, 0, 0, TimeSpan.Zero);
    public static readonly ScopePath Rivonia = ScopePath.Parse("shapers.campus_rivonia");
    public static readonly MembershipStatus Visitor = MembershipStatus.Create("Visitor", JourneyStage.Visitor, 10, isDefault: true);
    public static readonly MembershipStatus Member = MembershipStatus.Create("Member", JourneyStage.Member, 40);

    public static Person Person(string first = "Thandi", string last = "Mokoena", DateOnly? dob = null, string? mobile = null, string? email = null)
    {
        var person = Domain.Person.Create(Rivonia, first, last, Visitor, PersonSource.Admin, Now);
        person.SetDemographics(dob, null, Now);
        if (mobile is not null)
        {
            person.AddContact(ContactType.Mobile, mobile, isPrimary: true, isVerified: false, Now);
        }

        if (email is not null)
        {
            person.AddContact(ContactType.Email, email, isPrimary: true, isVerified: false, Now);
        }

        person.ClearDomainEvents();
        return person;
    }
}

public sealed class PersonTests
{
    [Fact]
    public void Creating_a_person_records_initial_status_and_raises_an_event()
    {
        var person = Person.Create(Build.Rivonia, "  Thandi ", "Mokoena", Build.Visitor, PersonSource.SelfRegistration, Build.Now);

        Assert.Equal("Thandi", person.FirstName);
        Assert.Single(person.StatusHistory);
        Assert.IsType<PersonCreated>(Assert.Single(person.DomainEvents));
    }

    [Fact]
    public void Status_changes_are_kept_as_history()
    {
        var person = Build.Person();

        person.ChangeMembershipStatus(Build.Member, new DateOnly(2026, 9, 1), Guid.NewGuid(), Build.Now);

        Assert.Equal(Build.Member.Id, person.MembershipStatusId);
        Assert.Equal(2, person.StatusHistory.Count);
        var changed = Assert.IsType<MembershipStatusChanged>(Assert.Single(person.DomainEvents));
        Assert.Equal(JourneyStage.Member, changed.ToStage);
    }

    [Fact]
    public void Only_one_primary_contact_per_type()
    {
        var person = Build.Person(mobile: "+27821234567");

        person.AddContact(ContactType.Mobile, "+27831234567", isPrimary: true, isVerified: false, Build.Now);

        Assert.Equal("+27831234567", person.PrimaryContact(ContactType.Mobile)!.Value);
        Assert.Single(person.Contacts, c => c.IsPrimary);
    }

    [Fact]
    public void Adding_an_existing_contact_updates_it_instead_of_duplicating()
    {
        var person = Build.Person(mobile: "+27821234567");

        person.AddContact(ContactType.Mobile, "+27821234567", isPrimary: false, isVerified: true, Build.Now);

        var contact = Assert.Single(person.Contacts);
        Assert.True(contact.IsVerified);
    }

    [Theory]
    [InlineData(ContactType.Mobile, "0821234567")]
    [InlineData(ContactType.Mobile, "+27 82 123 4567")]
    [InlineData(ContactType.Email, "Thandi@Example.com")]
    [InlineData(ContactType.Email, "not-an-email")]
    public void Contacts_must_already_be_normalised(ContactType type, string value)
    {
        var person = Build.Person();

        Assert.Throws<DomainRuleException>(() => person.AddContact(type, value, true, false, Build.Now));
    }

    [Fact]
    public void Removing_the_primary_contact_promotes_another()
    {
        var person = Build.Person(mobile: "+27821234567");
        person.AddContact(ContactType.Mobile, "+27831234567", isPrimary: false, isVerified: false, Build.Now);

        person.RemoveContact(person.PrimaryContact(ContactType.Mobile)!.Id, Build.Now);

        Assert.True(Assert.Single(person.Contacts).IsPrimary);
    }

    [Fact]
    public void Minors_are_identified_from_date_of_birth()
    {
        var today = new DateOnly(2026, 9, 28);

        Assert.True(Build.Person(dob: new DateOnly(2008, 9, 29)).IsMinorOn(today));
        Assert.False(Build.Person(dob: new DateOnly(2008, 9, 28)).IsMinorOn(today));
        Assert.False(Build.Person(dob: null).IsMinorOn(today));
    }

    [Fact]
    public void Date_of_birth_cannot_be_in_the_future()
    {
        Assert.Throws<DomainRuleException>(() => Build.Person().SetDemographics(new DateOnly(2030, 1, 1), null, Build.Now));
    }
}

public sealed class HouseholdTests
{
    [Fact]
    public void First_adult_becomes_primary_contact_and_children_cannot_be_primary()
    {
        var household = Household.Create("Mokoena", Build.Rivonia, Build.Now);
        var child = Guid.NewGuid();
        var adult = Guid.NewGuid();

        household.AddMember(child, HouseholdRole.Child);
        household.AddMember(adult, HouseholdRole.Adult);

        Assert.Equal(adult, household.PrimaryContactId);
        Assert.Throws<DomainRuleException>(() => household.SetPrimaryContact(child));
    }

    [Fact]
    public void Removing_the_primary_contact_hands_over_to_another_adult()
    {
        var household = Household.Create("Mokoena", Build.Rivonia, Build.Now);
        var mum = Guid.NewGuid();
        var dad = Guid.NewGuid();
        household.AddMember(mum, HouseholdRole.Adult);
        household.AddMember(dad, HouseholdRole.Adult);

        household.RemoveMember(mum);

        Assert.Equal(dad, household.PrimaryContactId);
    }

    [Fact]
    public void A_child_can_belong_to_two_households()
    {
        var child = Guid.NewGuid();
        var mumsHouse = Household.Create("Mokoena", Build.Rivonia, Build.Now);
        var dadsHouse = Household.Create("Dlamini", Build.Rivonia, Build.Now);

        mumsHouse.AddMember(child, HouseholdRole.Child);
        dadsHouse.AddMember(child, HouseholdRole.Child);

        Assert.Contains(mumsHouse.Members, m => m.PersonId == child);
        Assert.Contains(dadsHouse.Members, m => m.PersonId == child);
    }

    [Fact]
    public void Same_person_cannot_be_added_twice()
    {
        var household = Household.Create("Mokoena", Build.Rivonia, Build.Now);
        var id = Guid.NewGuid();
        household.AddMember(id, HouseholdRole.Adult);

        Assert.Throws<DomainRuleException>(() => household.AddMember(id, HouseholdRole.Child));
    }
}

public sealed class ConsentTests
{
    [Fact]
    public void Unknown_purposes_are_rejected()
    {
        Assert.Throws<DomainRuleException>(() =>
            ConsentRecord.Record(Guid.NewGuid(), "marketing.everything", true, LawfulBasis.Consent, "2026-09", ConsentSource.MobileApp, Build.Now, null));
    }

    [Fact]
    public void Current_position_is_the_latest_record_per_purpose()
    {
        var person = Guid.NewGuid();
        var history = new[]
        {
            ConsentRecord.Record(person, ConsentPurposes.WhatsAppCommunication, true, LawfulBasis.Consent, "2026-09", ConsentSource.MobileApp, Build.Now, null),
            ConsentRecord.Record(person, ConsentPurposes.WhatsAppCommunication, false, LawfulBasis.Consent, "2026-09", ConsentSource.MobileApp, Build.Now.AddDays(3), null),
            ConsentRecord.Record(person, ConsentPurposes.ChurchRecord, true, LawfulBasis.Consent, "2026-09", ConsentSource.MobileApp, Build.Now, null),
        };

        var current = ConsentRecord.Current(history);

        Assert.False(current[ConsentPurposes.WhatsAppCommunication].Granted);
        Assert.True(current[ConsentPurposes.ChurchRecord].Granted);
    }
}
