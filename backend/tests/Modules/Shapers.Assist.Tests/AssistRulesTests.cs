using Shapers.Assist.Application;
using Shapers.Assist.Domain;
using Shapers.SharedKernel;

namespace Shapers.Assist.Tests;

public sealed class AssistRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid User = Guid.NewGuid();

    [Theory]
    [InlineData("Email thandi@example.com for details", "an email address")]
    [InlineData("Call 082 123 4567 after the service", "a phone number")]
    [InlineData("Call +27 82 123 4567", "a phone number")]
    [InlineData("ID 9001015009087 on the form", "an ID number")]
    public void Guard_finds_contact_details(string text, string expected) =>
        Assert.Contains(expected, PersonalDataGuard.Find(text));

    [Theory]
    [InlineData("Join us on Sunday at 09:00 at 8 Mellis Road.")]
    [InlineData("Read James 2:14-17 and Psalm 42:1-11 before the meeting.")]
    [InlineData("Youth camp costs R450 per person, 12–14 December 2026.")]
    public void Guard_leaves_ordinary_church_text_alone(string text) =>
        Assert.Empty(PersonalDataGuard.Find(text));

    [Fact]
    public void Redact_removes_contact_details_but_keeps_the_message()
    {
        var redacted = PersonalDataGuard.Redact("If you need prayer, call 011 234 5678 or email info@shaperschurch.com. God is faithful.");

        Assert.DoesNotContain("011 234 5678", redacted);
        Assert.DoesNotContain("info@shaperschurch.com", redacted);
        Assert.Contains("God is faithful.", redacted);
    }

    [Fact]
    public void Masking_keeps_contact_details_out_and_puts_them_back()
    {
        var masked = new Dictionary<string, string>();
        var text = PersonalDataGuard.Mask("Email info@shaperschurch.com or call 011 234 5678. Again: info@shaperschurch.com", masked);

        Assert.Equal("Email [[C1]] or call [[C2]]. Again: [[C1]]", text);
        Assert.Equal("Thumela i-imeyili ku-info@shaperschurch.com noma ushayele 011 234 5678.", PersonalDataGuard.Unmask("Thumela i-imeyili ku-[[C1]] noma ushayele [[C2]].", masked));
    }

    [Fact]
    public void A_translation_answer_needs_a_title_and_text()
    {
        Assert.Throws<System.Text.Json.JsonException>(() => DraftReader.Translation("""{"title":"","summary":"","body":"x"}""", "zu"));

        var draft = DraftReader.Translation("""{"title":" Ukholo ","summary":"","body":"Umbhalo"}""", "zu");
        Assert.Equal("Ukholo", draft.Title);
        Assert.Equal("isiZulu", draft.LanguageName);
    }

    [Fact]
    public void A_draft_is_reviewed_once()
    {
        var draft = AiDraft.Create(DraftKind.Rewrite, "text", null, ScopePath.Parse("org"), "fake", Prompts.Rewrite, """{"text":"Hi"}""", User, Now);

        draft.Accept(User, Now);
        draft.Accept(User, Now);

        Assert.Equal(DraftStatus.Accepted, draft.Status);
        Assert.Throws<DomainRuleException>(() => draft.Discard(User, Now));
    }

    [Fact]
    public void An_empty_answer_is_not_a_draft() =>
        Assert.Throws<DomainRuleException>(() => AiDraft.Create(DraftKind.Rewrite, "text", null, ScopePath.Parse("org"), "fake", Prompts.Rewrite, " ", User, Now));

    [Fact]
    public void A_lesson_becomes_a_readable_body_for_leaders()
    {
        var lesson = DraftReader.Lesson("""
            {"title":" Faith that works ","summary":"Faith acts.","keyVerses":["James 2:14-17","James 2:14-17",""],"icebreaker":"Who helped you this week?","questions":["One?","Two?"],"application":"Help a neighbour.","prayerFocus":"Courage."}
            """);

        Assert.Equal("Faith that works", lesson.Title);
        Assert.Equal(["James 2:14-17"], lesson.KeyVerses);
        Assert.Contains("1. One?", lesson.Body);
        Assert.Contains("2. Two?", lesson.Body);
        Assert.Contains("Key verses: James 2:14-17", lesson.Body);
        Assert.EndsWith("Courage.", lesson.Body);
    }

    [Theory]
    [InlineData(DraftKind.SermonLesson, """{"title":"","summary":"x","keyVerses":[],"icebreaker":"","questions":[],"application":"","prayerFocus":""}""")]
    [InlineData(DraftKind.SermonNotes, "not json")]
    [InlineData(DraftKind.Rewrite, """{"text":""}""")]
    public void Unusable_answers_are_rejected(DraftKind kind, string json) =>
        Assert.False(DraftReader.IsValid(kind, json));

    [Fact]
    public void Notes_keep_at_most_six_topics()
    {
        var notes = DraftReader.Notes("""{"summary":"s","notes":"n","topics":["a","b","c","d","e","f","g"]}""");
        Assert.Equal(6, notes.Topics.Count);
    }

    [Theory]
    [InlineData(Prompts.SermonLesson, "{{transcript}}")]
    [InlineData(Prompts.SermonNotes, "{{transcript}}")]
    [InlineData(Prompts.Rewrite, "{{text}}")]
    public void Prompts_load_with_their_placeholders(string version, string placeholder)
    {
        var prompt = Prompts.Get(version);

        Assert.NotEmpty(prompt.System);
        Assert.Contains(placeholder, prompt.UserTemplate);
        Assert.DoesNotContain("=== user ===", prompt.System);
    }

    [Fact]
    public void Prompts_forbid_writing_out_bible_text()
    {
        Assert.Contains("Never write out the verse text", Prompts.Get(Prompts.SermonLesson).System);
        Assert.Contains("Never write out the verse text", Prompts.Get(Prompts.SermonNotes).System);
    }

    [Fact]
    public void Usage_never_records_negative_amounts()
    {
        var usage = AiUsage.Record(AiOperation.Chat, "rewrite", "fake", -5, 10, 0, -1m, null, true, Now);

        Assert.Equal(0, usage.InputTokens);
        Assert.Equal(0m, usage.CostZar);
    }
}
