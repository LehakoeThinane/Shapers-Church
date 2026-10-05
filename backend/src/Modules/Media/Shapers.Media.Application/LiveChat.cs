using Microsoft.EntityFrameworkCore;
using Shapers.Media.Contracts;
using Shapers.Media.Domain;
using Shapers.People.Contracts;
using Shapers.Platform.Auditing;
using Shapers.Platform.Authorization;

namespace Shapers.Media.Application;

/// <summary>What everyone watching sees. Carries no person ID: names are all the room needs.</summary>
public sealed record ChatMessageDto(Guid Id, string Author, bool FromTeam, string Text, DateTimeOffset SentAt, bool Pending = false, bool Mine = false);

public sealed record ChatRulesDto(bool Open, int SlowSeconds, bool ApprovalRequired);

public enum ChatBlock
{
    SignIn,
    Closed,
    Under18,
    TimedOut,
    Banned,
}

/// <summary>Whether the viewer can post, and if not, why (so the app can say so plainly).</summary>
public sealed record ChatMeDto(bool CanPost, ChatBlock? Blocked, bool IsModerator);

public sealed record ChatRoomDto(Guid LivestreamId, ChatRulesDto Rules, IReadOnlyList<ChatMessageDto> Messages, ChatMeDto Me);

public sealed record PostChatRequest(string Text);

public sealed record ModChatMessageDto(
    Guid Id,
    Guid PersonId,
    string Author,
    bool FromTeam,
    string Text,
    DateTimeOffset SentAt,
    ChatMessageStatus Status,
    ChatHoldReason? HoldReason,
    int Reports,
    DateTimeOffset? ModeratedAt);

public sealed record ChatSanctionDto(Guid Id, Guid PersonId, string Name, ChatSanctionKind Kind, string? Reason, DateTimeOffset CreatedAt, Guid LivestreamId);

public sealed record ModChatStreamDto(Guid Id, string Title, LivestreamStatus Status, DateTimeOffset ScheduledStart, bool ChatOpen);

public sealed record ModeratorRoomDto(
    Guid LivestreamId,
    string Title,
    LivestreamStatus Status,
    ChatRulesDto Rules,
    IReadOnlyList<ModChatMessageDto> Messages,
    IReadOnlyList<ChatSanctionDto> Sanctions);

public sealed record SanctionRequest(Guid PersonId, ChatSanctionKind Kind, string? Reason, bool HideMessages);

public sealed record ChatRulesRequest(int SlowSeconds, bool ApprovalRequired);

public sealed record WordListDto(IReadOnlyList<string> Terms);

/// <summary>
/// Livestream chat for members. Messages are saved before they are broadcast, so what moderators review is exactly
/// what people saw. Everyone can read; only signed-in adults who aren't timed out or banned can post.
/// </summary>
public sealed class LiveChatService(
    IMediaDb db,
    ICurrentUser currentUser,
    IAuthorizer authorizer,
    IPeopleDirectory people,
    IChatBroadcaster broadcaster,
    TimeProvider clock)
{
    public const int RoomSize = 100;

    private static readonly Error NotFound = Error.NotFound("media.stream_not_found", "Livestream not found.");

    public async Task<Result<ChatRoomDto>> RoomAsync(Guid livestreamId, CancellationToken cancellationToken)
    {
        var stream = await db.Livestreams.AsNoTracking().SingleOrDefaultAsync(s => s.Id == livestreamId, cancellationToken);
        if (stream is null || stream.Status == LivestreamStatus.Cancelled)
        {
            return NotFound;
        }

        var now = clock.GetUtcNow();
        var open = stream.IsChatOpen(now);
        var me = await MeAsync(stream, open, cancellationToken);
        var rules = Rules(stream, open);
        if (!open)
        {
            // Old chats aren't shown afterwards: the room was for the service.
            return new ChatRoomDto(stream.Id, rules, [], me);
        }

        var personId = currentUser.PersonId;
        var messages = await db.ChatMessages.AsNoTracking()
            .Where(m => m.LivestreamId == stream.Id && (m.Status == ChatMessageStatus.Visible || (personId != null && m.PersonId == personId && m.Status == ChatMessageStatus.Held)))
            .OrderByDescending(m => m.SentAt)
            .Take(RoomSize)
            .ToListAsync(cancellationToken);
        return new ChatRoomDto(
            stream.Id,
            rules,
            messages.OrderBy(m => m.SentAt).Select(m => ToDto(m, m.PersonId == personId)).ToList(),
            me);
    }

    public async Task<Result<ChatMessageDto>> PostAsync(Guid livestreamId, PostChatRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.PersonId is not { } personId)
        {
            return Error.Unauthorized("media.chat_sign_in", "Sign in to join the chat.");
        }

        var stream = await db.Livestreams.AsNoTracking().SingleOrDefaultAsync(s => s.Id == livestreamId, cancellationToken);
        if (stream is null || stream.Status == LivestreamStatus.Cancelled)
        {
            return NotFound;
        }

        var now = clock.GetUtcNow();
        var open = stream.IsChatOpen(now);
        var me = await MeAsync(stream, open, cancellationToken);
        if (!me.CanPost)
        {
            return Blocked(me.Blocked);
        }

        ChatHoldReason? hold = null;
        if (!me.IsModerator)
        {
            var last = await db.ChatMessages.AsNoTracking()
                .Where(m => m.LivestreamId == stream.Id && m.PersonId == personId)
                .MaxAsync(m => (DateTimeOffset?)m.SentAt, cancellationToken);
            if (last is { } previous && now - previous < TimeSpan.FromSeconds(stream.ChatSlowSeconds))
            {
                var wait = (int)Math.Ceiling((TimeSpan.FromSeconds(stream.ChatSlowSeconds) - (now - previous)).TotalSeconds);
                return Error.Conflict("media.chat_slow_mode", $"Slow mode is on. You can send another message in {wait} second{(wait == 1 ? string.Empty : "s")}.");
            }

            var terms = await db.ChatBlockedTerms.AsNoTracking().Select(t => t.Term).ToListAsync(cancellationToken);
            if (ChatRules.MatchesWordList(request.Text ?? string.Empty, terms))
            {
                hold = ChatHoldReason.WordList;
            }
            else if (stream.ChatApprovalRequired)
            {
                hold = ChatHoldReason.Approval;
            }
        }

        var person = await people.GetAsync(personId, cancellationToken);
        var message = ChatMessage.Post(stream.Id, stream.Scope, personId, ChatRules.ShortName(person?.DisplayName ?? string.Empty), me.IsModerator, request.Text ?? string.Empty, hold, now);
        db.ChatMessages.Add(message);
        await db.SaveChangesAsync(cancellationToken);

        var dto = ToDto(message, mine: true);
        if (message.Status == ChatMessageStatus.Visible)
        {
            await broadcaster.MessageAsync(stream.Id, dto with { Mine = false }, cancellationToken);
        }

        return dto;
    }

    public async Task<Result> ReportAsync(Guid livestreamId, Guid messageId, CancellationToken cancellationToken)
    {
        if (currentUser.PersonId is not { } personId)
        {
            return Error.Unauthorized("media.chat_sign_in", "Sign in to report a message.");
        }

        var message = await db.ChatMessages.SingleOrDefaultAsync(m => m.Id == messageId && m.LivestreamId == livestreamId, cancellationToken);
        if (message is null || message.Status == ChatMessageStatus.Hidden)
        {
            return Error.NotFound("media.chat_message_not_found", "That message is no longer in the chat.");
        }

        var held = message.Report(personId, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        if (held)
        {
            await broadcaster.RemovedAsync(livestreamId, message.Id, cancellationToken);
        }

        return Result.Success();
    }

    private async Task<ChatMeDto> MeAsync(Livestream stream, bool open, CancellationToken cancellationToken)
    {
        if (currentUser.PersonId is not { } personId)
        {
            return new ChatMeDto(false, ChatBlock.SignIn, false);
        }

        var moderator = await authorizer.CanAsync(MediaPermissions.ChatModerate, ScopePath.Parse(stream.Scope), cancellationToken);
        if (!open)
        {
            return new ChatMeDto(false, ChatBlock.Closed, moderator);
        }

        if (moderator)
        {
            return new ChatMeDto(true, null, true);
        }

        if (await people.IsMinorAsync(personId, cancellationToken))
        {
            return new ChatMeDto(false, ChatBlock.Under18, false);
        }

        var sanctions = await db.ChatSanctions.AsNoTracking()
            .Where(s => s.PersonId == personId && s.LiftedAt == null && (s.Kind == ChatSanctionKind.Ban || s.LivestreamId == stream.Id))
            .Select(s => s.Kind)
            .ToListAsync(cancellationToken);
        if (sanctions.Contains(ChatSanctionKind.Ban))
        {
            return new ChatMeDto(false, ChatBlock.Banned, false);
        }

        return sanctions.Count > 0 ? new ChatMeDto(false, ChatBlock.TimedOut, false) : new ChatMeDto(true, null, false);
    }

    private static Error Blocked(ChatBlock? block) => block switch
    {
        ChatBlock.SignIn => Error.Unauthorized("media.chat_sign_in", "Sign in to join the chat."),
        ChatBlock.Under18 => Error.Forbidden("media.chat_under_18", "The live chat is for adults for now. You're welcome to watch and read along."),
        ChatBlock.TimedOut => Error.Forbidden("media.chat_timed_out", "A moderator has paused your messages for the rest of this service."),
        ChatBlock.Banned => Error.Forbidden("media.chat_banned", "A moderator has stopped you from posting in the chat. Contact the church office if you think this is a mistake."),
        _ => new Error("media.chat_closed", "The chat is closed. It opens 15 minutes before a service."),
    };

    internal static ChatRulesDto Rules(Livestream stream, bool open) => new(open, stream.ChatSlowSeconds, stream.ChatApprovalRequired);

    internal static ChatMessageDto ToDto(ChatMessage m, bool mine) =>
        new(m.Id, m.AuthorName, m.FromTeam, m.Text, m.SentAt, m.Status == ChatMessageStatus.Held, mine);
}

/// <summary>The moderator console. Every action is audited and can be undone.</summary>
public sealed class ChatModerationService(
    IMediaDb db,
    ICurrentUser currentUser,
    IAuthorizer authorizer,
    IPeopleDirectory people,
    IChatBroadcaster broadcaster,
    IAuditLog audit,
    TimeProvider clock)
{
    public const int ConsoleSize = 300;
    public const int MaxTerms = 500;

    private static readonly Error NotFound = Error.NotFound("media.stream_not_found", "Livestream not found.");

    /// <summary>Services with chat open now, and recent or upcoming ones, in the moderator's scopes.</summary>
    public async Task<IReadOnlyList<ModChatStreamDto>> StreamsAsync(CancellationToken cancellationToken)
    {
        var scopes = await authorizer.ScopesForAsync(MediaPermissions.ChatModerate, cancellationToken);
        var now = clock.GetUtcNow();
        var from = now.AddDays(-2);
        var to = now.AddDays(7);
        var streams = await db.Livestreams.AsNoTracking()
            .WithinScopes(s => s.Scope, scopes)
            .Where(s => s.Status == LivestreamStatus.Live || (s.Status != LivestreamStatus.Cancelled && s.ScheduledStart >= from && s.ScheduledStart <= to))
            .OrderByDescending(s => s.Status == LivestreamStatus.Live).ThenBy(s => s.ScheduledStart)
            .Take(20)
            .ToListAsync(cancellationToken);
        return streams.Select(s => new ModChatStreamDto(s.Id, s.Title, s.Status, s.ScheduledStart, s.IsChatOpen(now))).ToList();
    }

    public async Task<Result<ModeratorRoomDto>> RoomAsync(Guid livestreamId, CancellationToken cancellationToken)
    {
        var stream = await StreamAsync(livestreamId, tracking: false, cancellationToken);
        if (stream is null)
        {
            return NotFound;
        }

        var messages = await db.ChatMessages.AsNoTracking()
            .Where(m => m.LivestreamId == livestreamId)
            .OrderByDescending(m => m.SentAt)
            .Take(ConsoleSize)
            .ToListAsync(cancellationToken);
        var sanctions = await db.ChatSanctions.AsNoTracking()
            .Where(s => s.LiftedAt == null && (s.Kind == ChatSanctionKind.Ban || s.LivestreamId == livestreamId))
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken);
        return new ModeratorRoomDto(
            stream.Id,
            stream.Title,
            stream.Status,
            LiveChatService.Rules(stream, stream.IsChatOpen(clock.GetUtcNow())),
            messages.OrderBy(m => m.SentAt).Select(ToModDto).ToList(),
            sanctions.Select(ToDto).ToList());
    }

    public Task<Result> HideAsync(Guid livestreamId, Guid messageId, CancellationToken cancellationToken) =>
        ModerateAsync(livestreamId, messageId, "media.chat.message_hidden", (message, moderator, now) => message.Hide(moderator, now), cancellationToken);

    public Task<Result> ShowAsync(Guid livestreamId, Guid messageId, CancellationToken cancellationToken) =>
        ModerateAsync(livestreamId, messageId, "media.chat.message_shown", (message, moderator, now) => message.Show(moderator, now), cancellationToken);

    public async Task<Result<ChatSanctionDto>> SanctionAsync(Guid livestreamId, SanctionRequest request, CancellationToken cancellationToken)
    {
        var stream = await StreamAsync(livestreamId, tracking: false, cancellationToken);
        if (stream is null || currentUser.PersonId is not { } moderator)
        {
            return NotFound;
        }

        var latest = await db.ChatMessages.AsNoTracking()
            .Where(m => m.PersonId == request.PersonId)
            .OrderByDescending(m => m.SentAt)
            .Select(m => m.AuthorName)
            .FirstOrDefaultAsync(cancellationToken);
        var name = latest ?? ChatRules.ShortName((await people.GetAsync(request.PersonId, cancellationToken))?.DisplayName ?? string.Empty);

        var now = clock.GetUtcNow();
        var sanction = ChatSanction.Impose(request.PersonId, request.Kind, stream.Id, name, request.Reason, moderator, now);
        db.ChatSanctions.Add(sanction);

        var hidden = new List<Guid>();
        if (request.HideMessages)
        {
            var theirs = await db.ChatMessages
                .Where(m => m.LivestreamId == stream.Id && m.PersonId == request.PersonId && m.Status != ChatMessageStatus.Hidden)
                .ToListAsync(cancellationToken);
            foreach (var message in theirs)
            {
                message.Hide(moderator, now);
                hidden.Add(message.Id);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(
            new AuditRecord($"media.chat.{(request.Kind == ChatSanctionKind.Ban ? "banned" : "timed_out")}", "chat_sanction", sanction.Id.ToString(), ScopePath.Parse(stream.Scope), new { request.PersonId, sanction.Reason, livestreamId, hiddenMessages = hidden.Count }),
            cancellationToken);
        foreach (var id in hidden)
        {
            await broadcaster.RemovedAsync(stream.Id, id, cancellationToken);
        }

        return ToDto(sanction);
    }

    public async Task<Result> LiftAsync(Guid sanctionId, CancellationToken cancellationToken)
    {
        var sanction = await db.ChatSanctions.SingleOrDefaultAsync(s => s.Id == sanctionId, cancellationToken);
        var stream = sanction is null ? null : await StreamAsync(sanction.LivestreamId, tracking: false, cancellationToken);
        if (sanction is null || stream is null || currentUser.PersonId is not { } moderator)
        {
            return Error.NotFound("media.chat_sanction_not_found", "That timeout or ban wasn't found.");
        }

        sanction.Lift(moderator, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord("media.chat.sanction_lifted", "chat_sanction", sanction.Id.ToString(), ScopePath.Parse(stream.Scope), new { sanction.PersonId, sanction.Kind }), cancellationToken);
        return Result.Success();
    }

    public async Task<Result<ChatRulesDto>> SetRulesAsync(Guid livestreamId, ChatRulesRequest request, CancellationToken cancellationToken)
    {
        var stream = await StreamAsync(livestreamId, tracking: true, cancellationToken);
        if (stream is null)
        {
            return NotFound;
        }

        var now = clock.GetUtcNow();
        stream.SetChatRules(request.SlowSeconds, request.ApprovalRequired, now);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord("media.chat.rules_changed", "livestream", stream.Id.ToString(), ScopePath.Parse(stream.Scope), request), cancellationToken);
        var rules = LiveChatService.Rules(stream, stream.IsChatOpen(now));
        await broadcaster.RulesAsync(stream.Id, rules, cancellationToken);
        return rules;
    }

    public async Task<WordListDto> WordsAsync(CancellationToken cancellationToken) =>
        new(await db.ChatBlockedTerms.AsNoTracking().OrderBy(t => t.Term).Select(t => t.Term).ToListAsync(cancellationToken));

    /// <summary>The word list is church-wide, so changing it needs chat moderation over the whole church.</summary>
    public async Task<Result<WordListDto>> SetWordsAsync(WordListDto request, CancellationToken cancellationToken)
    {
        var scopes = await authorizer.ScopesForAsync(MediaPermissions.ChatModerate, cancellationToken);
        if (!scopes.Any(s => !s.Value.Contains('.', StringComparison.Ordinal)))
        {
            return Error.Forbidden("media.forbidden", "Only church-wide chat moderators can change the word list.");
        }

        var terms = (request.Terms ?? [])
            .Select(t => t.Trim().ToLowerInvariant())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (terms.Count > MaxTerms || terms.Any(t => t.Length > 60))
        {
            return new Error("media.chat_word_list", $"Keep the list to {MaxTerms} entries of 60 characters at most.");
        }

        var existing = await db.ChatBlockedTerms.ToListAsync(cancellationToken);
        db.ChatBlockedTerms.RemoveRange(existing.Where(e => !terms.Contains(e.Term)));
        db.ChatBlockedTerms.AddRange(terms.Where(t => existing.All(e => e.Term != t)).Select(t => new ChatBlockedTerm(t)));
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord("media.chat.word_list_changed", "chat_word_list", null, null, new { count = terms.Count }), cancellationToken);
        return await WordsAsync(cancellationToken);
    }

    private async Task<Result> ModerateAsync(Guid livestreamId, Guid messageId, string action, Action<ChatMessage, Guid, DateTimeOffset> change, CancellationToken cancellationToken)
    {
        var stream = await StreamAsync(livestreamId, tracking: false, cancellationToken);
        var message = stream is null ? null : await db.ChatMessages.SingleOrDefaultAsync(m => m.Id == messageId && m.LivestreamId == livestreamId, cancellationToken);
        if (stream is null || message is null || currentUser.PersonId is not { } moderator)
        {
            return Error.NotFound("media.chat_message_not_found", "Message not found.");
        }

        var wasVisible = message.Status == ChatMessageStatus.Visible;
        change(message, moderator, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord(action, "chat_message", message.Id.ToString(), ScopePath.Parse(stream.Scope), new { livestreamId, message.PersonId }), cancellationToken);

        var isVisible = message.Status == ChatMessageStatus.Visible;
        if (wasVisible && !isVisible)
        {
            await broadcaster.RemovedAsync(livestreamId, message.Id, cancellationToken);
        }
        else if (!wasVisible && isVisible)
        {
            await broadcaster.MessageAsync(livestreamId, LiveChatService.ToDto(message, mine: false), cancellationToken);
        }

        return Result.Success();
    }

    private async Task<Livestream?> StreamAsync(Guid livestreamId, bool tracking, CancellationToken cancellationToken)
    {
        var query = tracking ? db.Livestreams : db.Livestreams.AsNoTracking();
        var stream = await query.SingleOrDefaultAsync(s => s.Id == livestreamId, cancellationToken);
        return stream is not null && await authorizer.CanAsync(MediaPermissions.ChatModerate, ScopePath.Parse(stream.Scope), cancellationToken) ? stream : null;
    }

    private static ModChatMessageDto ToModDto(ChatMessage m) =>
        new(m.Id, m.PersonId, m.AuthorName, m.FromTeam, m.Text, m.SentAt, m.Status, m.HoldReason, m.Reports.Count, m.ModeratedAt);

    private static ChatSanctionDto ToDto(ChatSanction s) => new(s.Id, s.PersonId, s.AuthorName, s.Kind, s.Reason, s.CreatedAt, s.LivestreamId);
}

/// <summary>Nightly: ordinary messages go after 90 days; evidence (hidden, held, reported, moderated) after a year.</summary>
public sealed class ChatRetention(IMediaDb db, TimeProvider clock)
{
    public async Task<int> PurgeAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var ordinary = now - ChatRules.Retention;
        var evidence = now - ChatRules.EvidenceRetention;

        var deleted = await db.ChatMessages.Where(m => m.SentAt < evidence).ExecuteDeleteAsync(cancellationToken);
        deleted += await db.ChatMessages
            .Where(m => m.SentAt < ordinary && m.Status == ChatMessageStatus.Visible && m.ModeratedBy == null && !m.Reports.Any())
            .ExecuteDeleteAsync(cancellationToken);

        // Timeouts end with their service; bans stay until lifted. Either is kept a year after it stopped applying.
        deleted += await db.ChatSanctions
            .Where(s => (s.LiftedAt != null && s.LiftedAt < evidence) || (s.Kind == ChatSanctionKind.Timeout && s.CreatedAt < evidence))
            .ExecuteDeleteAsync(cancellationToken);
        return deleted;
    }
}

/// <summary>A person's chat messages, for export and erasure. Erasure also removes their reports and any timeouts or bans on them.</summary>
public sealed class ChatPersonalData(IMediaDb db) : Shapers.Platform.Privacy.IPersonalDataSource
{
    public string Name => "Live chat";

    public async Task<object?> ExportAsync(Guid personId, CancellationToken cancellationToken)
    {
        var messages = await (from m in db.ChatMessages.AsNoTracking()
                              join s in db.Livestreams.AsNoTracking() on m.LivestreamId equals s.Id
                              where m.PersonId == personId
                              orderby m.SentAt
                              select new { Service = s.Title, m.Text, m.SentAt, Status = m.Status.ToString() })
            .ToListAsync(cancellationToken);
        var sanctions = await db.ChatSanctions.AsNoTracking()
            .Where(s => s.PersonId == personId)
            .Select(s => new { Kind = s.Kind.ToString(), s.Reason, s.CreatedAt, s.LiftedAt })
            .ToListAsync(cancellationToken);
        return messages.Count == 0 && sanctions.Count == 0 ? null : new { Messages = messages, TimeoutsAndBans = sanctions };
    }

    public async Task<int> EraseAsync(Guid personId, CancellationToken cancellationToken)
    {
        var reported = await db.ChatMessages.Where(m => m.Reports.Any(r => r.ReporterId == personId)).ToListAsync(cancellationToken);
        foreach (var message in reported)
        {
            message.ForgetReporter(personId);
        }

        await db.SaveChangesAsync(cancellationToken);
        return reported.Count
            + await db.ChatMessages.Where(m => m.PersonId == personId).ExecuteDeleteAsync(cancellationToken)
            + await db.ChatSanctions.Where(s => s.PersonId == personId).ExecuteDeleteAsync(cancellationToken);
    }
}
