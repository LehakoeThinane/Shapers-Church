using Shapers.Media.Domain;

namespace Shapers.Media.Tests;

public sealed class ChatTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 7, 0, 0, TimeSpan.Zero);
    private static readonly Guid Stream = Guid.CreateVersion7();
    private static readonly Guid Author = Guid.CreateVersion7();

    private static ChatMessage Message(string text = "Amen!", ChatHoldReason? hold = null) =>
        ChatMessage.Post(Stream, "shapers", Author, "Thabo M.", false, text, hold, Now);

    [Theory]
    [InlineData("Thabo Mokoena", "Thabo M.")]
    [InlineData("  Lerato   van der Merwe ", "Lerato M.")]
    [InlineData("Sipho", "Sipho")]
    [InlineData("", "Someone")]
    public void Names_show_as_first_name_and_last_initial(string displayName, string shown) =>
        Assert.Equal(shown, ChatRules.ShortName(displayName));

    [Fact]
    public void Messages_are_trimmed_and_limited_and_control_characters_removed()
    {
        Assert.Equal("Amen\nHallelujah", Message("  Amen‮\nHallelujah\u0007 ").Text);
        Assert.Equal("a\n\nb", Message("a\n\n\n\n\nb").Text);
        Assert.Throws<DomainRuleException>(() => Message("   "));
        Assert.Throws<DomainRuleException>(() => Message(new string('x', ChatRules.MaxLength + 1)));
        Assert.Equal(ChatRules.MaxLength, Message(new string('x', ChatRules.MaxLength)).Text.Length);
    }

    [Fact]
    public void Held_messages_wait_for_a_moderator()
    {
        var held = Message(hold: ChatHoldReason.WordList);
        Assert.Equal(ChatMessageStatus.Held, held.Status);

        held.Show(Guid.CreateVersion7(), Now);
        Assert.Equal(ChatMessageStatus.Visible, held.Status);
        Assert.Null(held.HoldReason);
    }

    [Fact]
    public void Three_different_reporters_hold_a_message_and_moderators_are_alerted_once()
    {
        var message = Message();
        var first = Guid.CreateVersion7();

        Assert.False(message.Report(first, Now));
        Assert.False(message.Report(first, Now));
        Assert.False(message.Report(Guid.CreateVersion7(), Now));
        Assert.Equal(ChatMessageStatus.Visible, message.Status);

        Assert.True(message.Report(Guid.CreateVersion7(), Now));
        Assert.Equal(ChatMessageStatus.Held, message.Status);
        Assert.Equal(ChatHoldReason.Reports, message.HoldReason);
        Assert.Equal(3, message.Reports.Count);
        Assert.Single(message.DomainEvents.OfType<ChatMessageReported>());
    }

    [Fact]
    public void A_message_a_moderator_restored_stays_visible_when_reported_again()
    {
        var message = Message();
        message.Show(Guid.CreateVersion7(), Now);

        for (var i = 0; i < ChatRules.ReportsToHold + 1; i++)
        {
            message.Report(Guid.CreateVersion7(), Now);
        }

        Assert.Equal(ChatMessageStatus.Visible, message.Status);
    }

    [Fact]
    public void Nobody_can_report_their_own_message()
    {
        Assert.Throws<DomainRuleException>(() => Message().Report(Author, Now));
    }

    [Fact]
    public void Erasing_a_reporter_removes_their_report()
    {
        var message = Message();
        var reporter = Guid.CreateVersion7();
        message.Report(reporter, Now);

        message.ForgetReporter(reporter);

        Assert.Empty(message.Reports);
    }

    [Theory]
    [InlineData("That was rubbish", true)]
    [InlineData("RUBBISH!", true)]
    [InlineData("rubbishy is not the word", false)]
    [InlineData("Praise God", false)]
    [InlineData("buy crypto now", true)]
    public void The_word_list_matches_whole_words_and_phrases_ignoring_case(string text, bool held) =>
        Assert.Equal(held, ChatRules.MatchesWordList(text, ["rubbish", "crypto now", " "]));

    [Fact]
    public void Chat_is_open_from_15_minutes_before_until_30_minutes_after_the_service()
    {
        var stream = Livestream.Schedule("Sunday service", ScopePath.Parse("shapers"), Now.AddHours(1), Now);
        stream.Update("Sunday service", Now.AddHours(1), new VideoLink(VideoProvider.YouTube, "y0jPz7KFw_o"), null, null, Now);

        Assert.False(stream.IsChatOpen(Now.AddMinutes(44)));
        Assert.True(stream.IsChatOpen(Now.AddMinutes(45)));
        Assert.False(stream.IsChatOpen(Now.AddHours(4).AddMinutes(1)));

        stream.GoLive(Now.AddHours(1));
        Assert.True(stream.IsChatOpen(Now.AddHours(1)));
        stream.End(Now.AddHours(2));
        Assert.True(stream.IsChatOpen(Now.AddHours(2).AddMinutes(30)));
        Assert.False(stream.IsChatOpen(Now.AddHours(2).AddMinutes(31)));
    }

    [Fact]
    public void Slow_mode_can_be_raised_but_not_switched_off()
    {
        var stream = Livestream.Schedule("Sunday service", ScopePath.Parse("shapers"), Now, Now);
        Assert.Equal(ChatRules.DefaultSlowSeconds, stream.ChatSlowSeconds);

        stream.SetChatRules(30, true, Now);
        Assert.Equal(30, stream.ChatSlowSeconds);
        Assert.True(stream.ChatApprovalRequired);

        Assert.Throws<DomainRuleException>(() => stream.SetChatRules(0, false, Now));
        Assert.Throws<DomainRuleException>(() => stream.SetChatRules(ChatRules.MaxSlowSeconds + 1, false, Now));
    }

    [Fact]
    public void A_timeout_covers_one_service_and_a_ban_covers_all_until_lifted()
    {
        var moderator = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        var timeout = ChatSanction.Impose(Author, ChatSanctionKind.Timeout, Stream, "Thabo M.", null, moderator, Now);
        var ban = ChatSanction.Impose(Author, ChatSanctionKind.Ban, Stream, "Thabo M.", "Spam", moderator, Now);

        Assert.True(timeout.AppliesTo(Stream));
        Assert.False(timeout.AppliesTo(other));
        Assert.True(ban.AppliesTo(other));

        ban.Lift(moderator, Now);
        Assert.False(ban.AppliesTo(other));
        Assert.Throws<DomainRuleException>(() => ChatSanction.Impose(moderator, ChatSanctionKind.Ban, Stream, "Me", null, moderator, Now));
    }
}
