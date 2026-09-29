using Shapers.Events.Domain;

namespace Shapers.Events.Tests;

internal static class Build
{
    public static readonly DateTimeOffset Now = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
    public static readonly ScopePath Church = ScopePath.Parse("shapers");

    public static Event Open(int? capacity = null, bool waitlist = false, int maxPerRegistration = 4)
    {
        var e = Event.Create("EPA In Person", Church, Now.AddDays(10), Now.AddDays(10).AddHours(3), Now);
        e.ConfigureRegistration(true, null, null, capacity, waitlist, maxPerRegistration, Now);
        e.Publish(Now);
        return e;
    }

    public static Registration Register(Event e, int seats, RegistrationStatus status, DateTimeOffset? at = null) =>
        Registration.Create(
            e,
            Guid.NewGuid(),
            Enumerable.Range(1, seats).Select(i => ((Guid?)null, $"Person {i}")).ToList(),
            new Dictionary<Guid, string>(),
            status,
            RegistrationSource.App,
            at ?? Now);
}

public sealed class SeatingTests
{
    [Fact]
    public void Without_a_capacity_everyone_is_confirmed()
    {
        var e = Build.Open(capacity: null);

        Assert.Equal(RegistrationStatus.Confirmed, Seating.Decide(e, seatsTaken: 5000, anyoneWaiting: false, requested: 4).Value);
    }

    [Fact]
    public void Fills_exactly_to_capacity()
    {
        var e = Build.Open(capacity: 10);

        Assert.Equal(RegistrationStatus.Confirmed, Seating.Decide(e, 7, false, 3).Value);
        Assert.Equal("events.full", Seating.Decide(e, 8, false, 3).Error!.Code);
    }

    [Fact]
    public void Joins_the_waiting_list_when_full_and_the_list_is_enabled()
    {
        var e = Build.Open(capacity: 10, waitlist: true);

        Assert.Equal(RegistrationStatus.Waitlisted, Seating.Decide(e, 10, false, 1).Value);
    }

    [Fact]
    public void A_new_booking_never_jumps_the_queue()
    {
        var e = Build.Open(capacity: 10, waitlist: true);

        // Two seats are free, but someone is already waiting: the newcomer waits too.
        Assert.Equal(RegistrationStatus.Waitlisted, Seating.Decide(e, 8, anyoneWaiting: true, requested: 1).Value);
    }

    [Fact]
    public void A_party_larger_than_the_venue_is_refused_outright()
    {
        var e = Build.Open(capacity: 3, waitlist: true);

        Assert.Equal("events.party_too_big", Seating.Decide(e, 0, false, 4).Error!.Code);
    }

    [Fact]
    public void Promotion_is_first_come_first_served_and_stops_at_a_party_that_does_not_fit()
    {
        var e = Build.Open(capacity: 10, waitlist: true);
        var first = Build.Register(e, 2, RegistrationStatus.Waitlisted, Build.Now.AddMinutes(1));
        var second = Build.Register(e, 3, RegistrationStatus.Waitlisted, Build.Now.AddMinutes(2));
        var third = Build.Register(e, 1, RegistrationStatus.Waitlisted, Build.Now.AddMinutes(3));

        // Four seats free: the first (2) fits, the second (3) doesn't, so the third waits its turn too.
        var promoted = Seating.ToPromote(e, seatsTaken: 6, [first, second, third]);

        Assert.Equal([first], promoted);
    }

    [Fact]
    public void Seats_left_never_goes_negative()
    {
        var e = Build.Open(capacity: 10);

        Assert.Equal(0, Seating.SeatsLeft(e, 12));
        Assert.Equal(int.MaxValue, Seating.SeatsLeft(Build.Open(capacity: null), 12));
    }
}

public sealed class RegistrationTests
{
    [Fact]
    public void Each_attendee_gets_their_own_unguessable_ticket()
    {
        var registration = Build.Register(Build.Open(), 3, RegistrationStatus.Confirmed);

        Assert.Equal(3, registration.Attendees.Select(a => a.TicketCode).Distinct().Count());
        Assert.All(registration.Attendees, a => Assert.Matches("^[A-HJ-NP-Z2-9]{10}$", a.TicketCode));
    }

    [Fact]
    public void Party_size_is_limited_by_the_event()
    {
        var e = Build.Open(maxPerRegistration: 2);

        Assert.Throws<DomainRuleException>(() => Build.Register(e, 3, RegistrationStatus.Confirmed));
    }

    [Fact]
    public void Checking_in_twice_is_harmless_and_reported()
    {
        var registration = Build.Register(Build.Open(), 1, RegistrationStatus.Confirmed);
        var attendee = registration.Attendees[0];

        Assert.Equal(CheckInOutcome.CheckedIn, registration.CheckIn(attendee.Id, null, Build.Now).Outcome);
        var second = registration.CheckIn(attendee.Id, null, Build.Now.AddMinutes(5));

        Assert.Equal(CheckInOutcome.AlreadyCheckedIn, second.Outcome);
        Assert.Equal(Build.Now, second.Attendee.CheckedInAt);
    }

    [Fact]
    public void Waitlisted_and_cancelled_tickets_do_not_get_in()
    {
        var e = Build.Open(capacity: 1, waitlist: true);
        var waiting = Build.Register(e, 1, RegistrationStatus.Waitlisted);
        var cancelled = Build.Register(e, 1, RegistrationStatus.Confirmed);
        cancelled.Cancel(Build.Now);

        Assert.Equal(CheckInOutcome.NotConfirmed, waiting.CheckIn(waiting.Attendees[0].Id, null, Build.Now).Outcome);
        Assert.Equal(CheckInOutcome.NotConfirmed, cancelled.CheckIn(cancelled.Attendees[0].Id, null, Build.Now).Outcome);
    }

    [Fact]
    public void Cannot_cancel_after_someone_has_checked_in()
    {
        var registration = Build.Register(Build.Open(), 2, RegistrationStatus.Confirmed);
        registration.CheckIn(registration.Attendees[0].Id, null, Build.Now);

        Assert.Throws<DomainRuleException>(() => registration.Cancel(Build.Now));
    }

    [Fact]
    public void Promotion_confirms_and_raises_an_event()
    {
        var registration = Build.Register(Build.Open(capacity: 1, waitlist: true), 1, RegistrationStatus.Waitlisted);
        registration.ClearDomainEvents();

        registration.Promote(Build.Now);

        Assert.Equal(RegistrationStatus.Confirmed, registration.Status);
        Assert.IsType<WaitlistPromoted>(Assert.Single(registration.DomainEvents));
    }

    [Theory]
    [InlineData("shapers-t:abcd234567", "ABCD234567")]
    [InlineData("  ABCD234567 ", "ABCD234567")]
    public void Scanned_codes_are_normalised(string scanned, string expected)
    {
        Assert.Equal(expected, TicketCode.Normalise(scanned));
    }
}

public sealed class EventTests
{
    [Fact]
    public void Registration_is_open_only_while_published_and_inside_the_window()
    {
        var e = Event.Create("Conference", Build.Church, Build.Now.AddDays(5), Build.Now.AddDays(6), Build.Now);
        e.ConfigureRegistration(true, Build.Now.AddDays(1), null, null, false, 1, Build.Now);

        Assert.NotNull(e.RegistrationClosedReason(Build.Now.AddDays(2)));
        e.Publish(Build.Now);
        Assert.Contains("opens", e.RegistrationClosedReason(Build.Now), StringComparison.Ordinal);
        Assert.Null(e.RegistrationClosedReason(Build.Now.AddDays(2)));
        Assert.Contains("closed", e.RegistrationClosedReason(Build.Now.AddDays(5)), StringComparison.Ordinal);

        e.Cancel(Build.Now);
        Assert.Contains("cancelled", e.RegistrationClosedReason(Build.Now.AddDays(2)), StringComparison.Ordinal);
    }

    [Fact]
    public void An_event_must_end_after_it_starts()
    {
        Assert.Throws<DomainRuleException>(() => Event.Create("Backwards", Build.Church, Build.Now, Build.Now.AddHours(-1), Build.Now));
    }

    [Fact]
    public void Required_questions_must_be_answered_and_unknown_answers_are_refused()
    {
        var e = Build.Open();
        e.SetQuestions([(null, "Dietary requirements", true), (null, "Church you attend", false)], Build.Now);
        var dietary = e.Questions[0].Id;

        Assert.Throws<DomainRuleException>(() => e.ValidateAnswers(new Dictionary<Guid, string>()));
        Assert.Throws<DomainRuleException>(() => e.ValidateAnswers(new Dictionary<Guid, string> { [dietary] = "None", [Guid.NewGuid()] = "x" }));

        var answers = e.ValidateAnswers(new Dictionary<Guid, string> { [dietary] = "  Vegetarian ", [e.Questions[1].Id] = " " });
        Assert.Equal("Vegetarian", Assert.Single(answers).Value);
    }

    [Fact]
    public void Question_ids_survive_edits_so_earlier_answers_stay_linked()
    {
        var e = Build.Open();
        e.SetQuestions([(null, "T-shirt size", false)], Build.Now);
        var id = e.Questions[0].Id;

        e.SetQuestions([(id, "T-shirt size (S-XL)", false)], Build.Now);

        Assert.Equal(id, e.Questions[0].Id);
    }

    [Fact]
    public void A_waiting_list_needs_a_capacity()
    {
        var e = Build.Open(capacity: null, waitlist: true);

        Assert.False(e.WaitlistEnabled);
    }

    [Fact]
    public void Email_codes_work_once_and_lock_after_too_many_tries()
    {
        var verification = EmailVerification.Create(Guid.NewGuid(), "guest@example.com", "RIGHT", Build.Now);
        for (var i = 1; i < EmailVerification.MaxAttempts; i++)
        {
            Assert.False(verification.TryUse("WRONG", Build.Now));
        }

        Assert.True(verification.TryUse("RIGHT", Build.Now));
        Assert.False(verification.TryUse("RIGHT", Build.Now));
    }
}
