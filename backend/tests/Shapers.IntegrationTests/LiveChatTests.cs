using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shapers.Communications.Application;
using Shapers.Identity.Application;
using Shapers.Media.Api;
using Shapers.Media.Application;
using Shapers.Media.Domain;
using Shapers.People.Application;
using Shapers.People.Contracts;
using Shapers.People.Domain;

namespace Shapers.IntegrationTests;

public sealed class LiveChatTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_members_message_reaches_everyone_watching_straight_away()
    {
        var admin = await api.SignInAdminAsync();
        var stream = await OpenStreamAsync(admin);
        var member = await SignInMemberAsync("082 555 4001", "+27825554001", "Thabo", "Mokoena");

        var received = new ConcurrentQueue<JsonElement>();
        await using var watcher = new HubConnectionBuilder()
            .WithUrl(new Uri(api.Server.BaseAddress, LiveChatHub.Path), o =>
            {
                o.HttpMessageHandlerFactory = _ => api.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
            })
            .Build();
        watcher.On<JsonElement>("message", received.Enqueue);
        await watcher.StartAsync(Ct);
        await watcher.InvokeAsync("Join", stream.Id, Ct);

        var sent = await (await member.PostJsonAsync(ChatUrl(stream.Id), new PostChatRequest("Amen! Watching from Soweto."))).ReadAsync<ChatMessageDto>();
        Assert.Equal("Thabo M.", sent.Author);
        Assert.True(sent.Mine);
        Assert.False(sent.Pending);

        for (var attempt = 0; attempt < 40 && received.IsEmpty; attempt++)
        {
            await Task.Delay(100, Ct);
        }

        var pushed = Assert.Single(received);
        Assert.Equal("Amen! Watching from Soweto.", pushed.GetProperty("text").GetString());
        Assert.Equal("Thabo M.", pushed.GetProperty("author").GetString());
        Assert.False(pushed.TryGetProperty("personId", out _));

        // Guests can read along but not post.
        var guest = api.Browser();
        var room = await (await guest.GetAsync(ChatUrl(stream.Id))).ReadAsync<ChatRoomDto>();
        Assert.Contains(room.Messages, m => m.Id == sent.Id && !m.Mine);
        Assert.Equal(ChatBlock.SignIn, room.Me.Blocked);
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.PostJsonAsync(ChatUrl(stream.Id), new PostChatRequest("Hi"))).StatusCode);

        // Slow mode: a second message straight away waits.
        var tooSoon = await member.PostJsonAsync(ChatUrl(stream.Id), new PostChatRequest("And again"));
        Assert.Equal(HttpStatusCode.Conflict, tooSoon.StatusCode);
        Assert.Contains("media.chat_slow_mode", await tooSoon.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

        // Moderators aren't slowed down, and carry the team badge.
        var first = await (await admin.PostJsonAsync(ChatUrl(stream.Id), new PostChatRequest("Welcome, everyone!"))).ReadAsync<ChatMessageDto>();
        var second = await (await admin.PostJsonAsync(ChatUrl(stream.Id), new PostChatRequest("Notes are on the Notes tab."))).ReadAsync<ChatMessageDto>();
        Assert.True(first.FromTeam && second.FromTeam);
    }

    [Fact]
    public async Task Under_18s_can_read_but_not_post()
    {
        var admin = await api.SignInAdminAsync();
        var stream = await OpenStreamAsync(admin);
        var teen = await SignInMemberAsync("082 555 4002", "+27825554002", "Kea", "Molefe");
        var birthday = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-15));
        (await teen.PutAsJsonAsync("/api/me/profile", new UpdateMyProfileRequest("Kea", "Molefe", null, birthday), ApiFactory.Json, Ct)).EnsureSuccessStatusCode();

        var room = await (await teen.GetAsync(ChatUrl(stream.Id))).ReadAsync<ChatRoomDto>();
        Assert.False(room.Me.CanPost);
        Assert.Equal(ChatBlock.Under18, room.Me.Blocked);

        var refused = await teen.PostJsonAsync(ChatUrl(stream.Id), new PostChatRequest("Hi!"));
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Contains("media.chat_under_18", await refused.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Three_reports_hide_a_message_alert_the_moderators_and_a_moderator_can_restore_it()
    {
        var admin = await api.SignInAdminAsync();
        var stream = await OpenStreamAsync(admin);
        var author = await SignInMemberAsync("082 555 4003", "+27825554003", "Pieter", "Botha");
        var message = await (await author.PostJsonAsync(ChatUrl(stream.Id), new PostChatRequest("Check out my page for cheap deals"))).ReadAsync<ChatMessageDto>();

        Assert.Equal(HttpStatusCode.BadRequest, (await author.PostAsync($"{ChatUrl(stream.Id)}/{message.Id}/report", null, Ct)).StatusCode);
        for (var i = 0; i < 3; i++)
        {
            var reporter = await SignInMemberAsync($"082 555 401{i}", $"+2782555401{i}", "Reporter", $"Number{i}");
            (await reporter.PostAsync($"{ChatUrl(stream.Id)}/{message.Id}/report", null, Ct)).EnsureSuccessStatusCode();
        }

        var room = await (await api.Browser().GetAsync(ChatUrl(stream.Id))).ReadAsync<ChatRoomDto>();
        Assert.DoesNotContain(room.Messages, m => m.Id == message.Id);

        // The author still sees it, marked as waiting.
        var theirs = await (await author.GetAsync(ChatUrl(stream.Id))).ReadAsync<ChatRoomDto>();
        Assert.True(Assert.Single(theirs.Messages, m => m.Id == message.Id).Pending);

        var alert = await WaitForAsync(admin, n => n.Title == "Chat message reported");
        Assert.DoesNotContain("cheap deals", alert.Body, StringComparison.Ordinal);

        var console = await (await admin.GetAsync($"/api/admin/media/chat/{stream.Id}")).ReadAsync<ModeratorRoomDto>();
        var held = Assert.Single(console.Messages, m => m.Id == message.Id);
        Assert.Equal(ChatMessageStatus.Held, held.Status);
        Assert.Equal(ChatHoldReason.Reports, held.HoldReason);
        Assert.Equal(3, held.Reports);

        (await admin.PostAsync($"/api/admin/media/chat/{stream.Id}/messages/{message.Id}/show", null, Ct)).EnsureSuccessStatusCode();
        room = await (await api.Browser().GetAsync(ChatUrl(stream.Id))).ReadAsync<ChatRoomDto>();
        Assert.Contains(room.Messages, m => m.Id == message.Id);

        // Members can't reach the console.
        Assert.Equal(HttpStatusCode.Forbidden, (await author.GetAsync($"/api/admin/media/chat/{stream.Id}")).StatusCode);
    }

    [Fact]
    public async Task Moderators_hold_listed_words_approve_messages_and_time_out_or_ban_people()
    {
        var admin = await api.SignInAdminAsync();
        var stream = await OpenStreamAsync(admin);
        var member = await SignInMemberAsync("082 555 4020", "+27825554020", "Naledi", "Khumalo");
        var profile = await (await member.GetAsync("/api/me/profile")).ReadAsync<PersonDetailDto>();

        // The word list holds a message back for review.
        (await admin.PutAsJsonAsync("/api/admin/media/chat/words", new WordListDto(["Crypto Now", "rubbish"]), ApiFactory.Json, Ct)).EnsureSuccessStatusCode();
        var words = await (await admin.GetAsync("/api/admin/media/chat/words")).ReadAsync<WordListDto>();
        Assert.Equal(["crypto now", "rubbish"], words.Terms);
        var flagged = await (await member.PostJsonAsync(ChatUrl(stream.Id), new PostChatRequest("buy CRYPTO NOW"))).ReadAsync<ChatMessageDto>();
        Assert.True(flagged.Pending);
        Assert.DoesNotContain((await (await api.Browser().GetAsync(ChatUrl(stream.Id))).ReadAsync<ChatRoomDto>()).Messages, m => m.Id == flagged.Id);

        // "Approve every message" mode, with slow mode kept short for the test.
        var rules = await (await admin.PutAsJsonAsync($"/api/admin/media/chat/{stream.Id}/rules", new ChatRulesRequest(5, true), ApiFactory.Json, Ct)).ReadAsync<ChatRulesDto>();
        Assert.True(rules.ApprovalRequired);
        await Task.Delay(TimeSpan.FromSeconds(5.2), Ct);
        var waiting = await (await member.PostJsonAsync(ChatUrl(stream.Id), new PostChatRequest("Good morning church"))).ReadAsync<ChatMessageDto>();
        Assert.True(waiting.Pending);
        (await admin.PostAsync($"/api/admin/media/chat/{stream.Id}/messages/{waiting.Id}/show", null, Ct)).EnsureSuccessStatusCode();
        Assert.Contains((await (await api.Browser().GetAsync(ChatUrl(stream.Id))).ReadAsync<ChatRoomDto>()).Messages, m => m.Id == waiting.Id);

        // A timeout stops posting for this service and can hide what they said.
        var timeout = await (await admin.PostJsonAsync($"/api/admin/media/chat/{stream.Id}/sanctions", new SanctionRequest(profile.Id, ChatSanctionKind.Timeout, "Off topic", true)))
            .ReadAsync<ChatSanctionDto>();
        Assert.Equal("Naledi K.", timeout.Name);
        Assert.DoesNotContain((await (await api.Browser().GetAsync(ChatUrl(stream.Id))).ReadAsync<ChatRoomDto>()).Messages, m => m.Id == waiting.Id);
        var timedOut = await member.PostJsonAsync(ChatUrl(stream.Id), new PostChatRequest("Hello?"));
        Assert.Equal(HttpStatusCode.Forbidden, timedOut.StatusCode);
        Assert.Contains("media.chat_timed_out", await timedOut.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

        // It doesn't follow them to the next service; a ban does, until lifted.
        var next = await OpenStreamAsync(admin);
        Assert.True((await (await member.GetAsync(ChatUrl(next.Id))).ReadAsync<ChatRoomDto>()).Me.CanPost);
        var ban = await (await admin.PostJsonAsync($"/api/admin/media/chat/{stream.Id}/sanctions", new SanctionRequest(profile.Id, ChatSanctionKind.Ban, "Spam", false)))
            .ReadAsync<ChatSanctionDto>();
        Assert.Equal(ChatBlock.Banned, (await (await member.GetAsync(ChatUrl(next.Id))).ReadAsync<ChatRoomDto>()).Me.Blocked);
        (await admin.PostAsync($"/api/admin/media/chat/sanctions/{ban.Id}/lift", null, Ct)).EnsureSuccessStatusCode();
        Assert.True((await (await member.GetAsync(ChatUrl(next.Id))).ReadAsync<ChatRoomDto>()).Me.CanPost);

        // Every moderation step is in the audit log.
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Shapers.Platform.Persistence.PlatformDbContext>();
        var actions = await db.AuditEntries.AsNoTracking().Where(a => a.Action.StartsWith("media.chat.")).Select(a => a.Action).ToListAsync(Ct);
        Assert.Contains("media.chat.message_shown", actions);
        Assert.Contains("media.chat.timed_out", actions);
        Assert.Contains("media.chat.banned", actions);
        Assert.Contains("media.chat.sanction_lifted", actions);
        Assert.Contains("media.chat.rules_changed", actions);
        Assert.Contains("media.chat.word_list_changed", actions);

        (await admin.PutAsJsonAsync("/api/admin/media/chat/words", new WordListDto([]), ApiFactory.Json, Ct)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task A_finished_service_no_longer_shows_its_chat_or_takes_messages()
    {
        var admin = await api.SignInAdminAsync();
        var stream = await (await admin.PostJsonAsync("/api/admin/media/livestreams", new SaveLivestreamRequest(
            "Next week", DateTimeOffset.UtcNow.AddDays(3), "https://www.youtube.com/live/y0jPz7KFw_o", null, null, null))).ReadAsync<LivestreamAdminDto>();
        var member = await SignInMemberAsync("082 555 4030", "+27825554030", "Early", "Bird");

        var room = await (await member.GetAsync(ChatUrl(stream.Id))).ReadAsync<ChatRoomDto>();
        Assert.False(room.Rules.Open);
        Assert.Equal(ChatBlock.Closed, room.Me.Blocked);
        Assert.Equal(HttpStatusCode.BadRequest, (await member.PostJsonAsync(ChatUrl(stream.Id), new PostChatRequest("Too early"))).StatusCode);
    }

    [Fact]
    public async Task Retention_keeps_evidence_for_a_year_and_ordinary_messages_for_90_days()
    {
        var old = DateTimeOffset.UtcNow.AddDays(-100);
        var ancient = DateTimeOffset.UtcNow.AddDays(-400);
        var stream = Guid.CreateVersion7();
        var person = Guid.CreateVersion7();

        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IMediaDb>();
        var ordinary = ChatMessage.Post(stream, "shapers", person, "Old T.", false, "Old amen", null, old);
        var reported = ChatMessage.Post(stream, "shapers", person, "Old T.", false, "Reported", null, old);
        reported.Report(Guid.CreateVersion7(), old);
        var hidden = ChatMessage.Post(stream, "shapers", person, "Old T.", false, "Hidden", null, old);
        hidden.Hide(Guid.CreateVersion7(), old);
        var expired = ChatMessage.Post(stream, "shapers", person, "Old T.", false, "Very old", null, ancient);
        expired.Report(Guid.CreateVersion7(), ancient);
        var recent = ChatMessage.Post(stream, "shapers", person, "Old T.", false, "Recent", null, DateTimeOffset.UtcNow);
        db.ChatMessages.AddRange(ordinary, reported, hidden, expired, recent);
        await db.SaveChangesAsync(Ct);

        await scope.ServiceProvider.GetRequiredService<ChatRetention>().PurgeAsync(Ct);

        var left = await db.ChatMessages.AsNoTracking().Where(m => m.LivestreamId == stream).Select(m => m.Text).ToListAsync(Ct);
        Assert.Equal(["Hidden", "Recent", "Reported"], left.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_members_chat_is_in_their_data_export_and_goes_when_they_are_erased()
    {
        var admin = await api.SignInAdminAsync();
        var stream = await OpenStreamAsync(admin);
        var member = await SignInMemberAsync("082 555 4040", "+27825554040", "Zanele", "Ndlovu");
        var profile = await (await member.GetAsync("/api/me/profile")).ReadAsync<PersonDetailDto>();
        await (await member.PostJsonAsync(ChatUrl(stream.Id), new PostChatRequest("Blessed Sunday!"))).ReadAsync<ChatMessageDto>();

        var export = await (await member.GetAsync("/api/me/data-export")).Content.ReadAsStringAsync(Ct);
        Assert.Contains("Live chat", export, StringComparison.Ordinal);
        Assert.Contains("Blessed Sunday!", export, StringComparison.Ordinal);

        await using var scope = api.Services.CreateAsyncScope();
        var chat = scope.ServiceProvider.GetServices<Shapers.Platform.Privacy.IPersonalDataSource>().OfType<ChatPersonalData>().Single();
        var erased = await chat.EraseAsync(profile.Id, Ct);
        Assert.Equal(1, erased);
        Assert.DoesNotContain((await (await api.Browser().GetAsync(ChatUrl(stream.Id))).ReadAsync<ChatRoomDto>()).Messages, m => m.Text == "Blessed Sunday!");
    }

    private static string ChatUrl(Guid streamId) => $"/api/media/live/{streamId}/chat";

    /// <summary>A service starting in ten minutes: inside the 15-minute window, so its chat is open.</summary>
    private static async Task<LivestreamAdminDto> OpenStreamAsync(HttpClient admin) =>
        await (await admin.PostJsonAsync("/api/admin/media/livestreams", new SaveLivestreamRequest(
            "Sunday service", DateTimeOffset.UtcNow.AddMinutes(10), "https://www.youtube.com/live/y0jPz7KFw_o", null, null, null))).ReadAsync<LivestreamAdminDto>();

    private static async Task<NotificationDto> WaitForAsync(HttpClient client, Func<NotificationDto, bool> match)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var inbox = await (await client.GetAsync("/api/me/notifications")).ReadAsync<InboxDto>();
            if (inbox.Items.FirstOrDefault(match) is { } found)
            {
                return found;
            }

            await Task.Delay(250, Ct);
        }

        throw new TimeoutException("The notification never arrived.");
    }

    private async Task<HttpClient> SignInMemberAsync(string phone, string e164, string first, string last)
    {
        var client = api.Browser();
        var request = await (await client.PostJsonAsync("/api/auth/otp/request", new { phone })).ReadAsync<RequestCodeResponse>();
        var verified = await (await client.PostJsonAsync("/api/auth/otp/verify", new { request.ChallengeId, code = api.Sms.LastCodeFor(e164) })).ReadAsync<SignInResponse>();
        var signedIn = await (await client.PostJsonAsync("/api/auth/register", new
        {
            request.ChallengeId,
            registrationTicket = verified.RegistrationTicket,
            firstName = first,
            lastName = last,
            policyVersion = "2026-09",
            consents = new[] { new ConsentDecision(ConsentPurposes.ChurchRecord, true) },
        })).ReadAsync<SignInResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", signedIn.Tokens!.AccessToken);
        return client;
    }
}
