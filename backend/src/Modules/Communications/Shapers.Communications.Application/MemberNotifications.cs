using Microsoft.EntityFrameworkCore;
using Shapers.Communications.Domain;
using Shapers.People.Contracts;
using Shapers.Platform.Authorization;

namespace Shapers.Communications.Application;

/// <summary>A member's devices, inbox and notification settings.</summary>
public sealed class MemberNotifications(ICommunicationsDb db, IPeopleDirectory people, ICurrentUser currentUser, TimeProvider clock)
{
    private static readonly Error SignIn = Error.Unauthorized("communications.sign_in", "Sign in first.");

    /// <summary>The app calls this on every start once notifications are allowed. Registering twice is harmless.</summary>
    public async Task<Result> RegisterDeviceAsync(RegisterDeviceRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.PersonId is not { } me)
        {
            return SignIn;
        }

        if (!Device.IsExpoToken(request.Token))
        {
            return new Error("communications.token_invalid", "That isn't a push token this app issues.");
        }

        var now = clock.GetUtcNow();
        var device = await db.Devices.SingleOrDefaultAsync(d => d.Token == request.Token, cancellationToken);
        if (device is null)
        {
            db.Devices.Add(Device.Register(me, request.Token, request.Platform, request.Name, now));
        }
        else
        {
            device.Refresh(me, request.Name, now);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>On sign-out: this phone stops receiving the member's notifications.</summary>
    public async Task<Result> UnregisterDeviceAsync(UnregisterDeviceRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.PersonId is not { } me)
        {
            return SignIn;
        }

        var device = await db.Devices.SingleOrDefaultAsync(d => d.Token == request.Token && d.PersonId == me, cancellationToken);
        if (device is not null)
        {
            device.Disable("Signed out", clock.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
    }

    public async Task<Result<InboxDto>> InboxAsync(int page, CancellationToken cancellationToken)
    {
        if (currentUser.PersonId is not { } me)
        {
            return SignIn;
        }

        const int pageSize = 30;
        var items = await db.Notifications.AsNoTracking()
            .Where(n => n.PersonId == me)
            .OrderByDescending(n => n.CreatedAt)
            .Skip(Math.Max(0, page - 1) * pageSize)
            .Take(pageSize)
            .Select(n => new NotificationDto(n.Id, n.Topic, n.Title, n.Body, n.Link, n.CreatedAt, n.ReadAt != null))
            .ToListAsync(cancellationToken);
        var unread = await db.Notifications.CountAsync(n => n.PersonId == me && n.ReadAt == null, cancellationToken);
        return new InboxDto(items, unread);
    }

    public async Task<Result> MarkReadAsync(Guid? id, CancellationToken cancellationToken)
    {
        if (currentUser.PersonId is not { } me)
        {
            return SignIn;
        }

        var unread = await db.Notifications
            .Where(n => n.PersonId == me && n.ReadAt == null && (id == null || n.Id == id))
            .ToListAsync(cancellationToken);
        var now = clock.GetUtcNow();
        unread.ForEach(n => n.MarkRead(now));
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<PreferenceDto>>> PreferencesAsync(CancellationToken cancellationToken)
    {
        if (currentUser.PersonId is not { } me)
        {
            return SignIn;
        }

        var saved = await db.Preferences.AsNoTracking().Where(p => p.PersonId == me).ToListAsync(cancellationToken);
        var pushConsent = (await people.WithConsentAsync([me], CommunicationConsents.Push, cancellationToken)).Contains(me);
        var emailConsent = (await people.WithConsentAsync([me], CommunicationConsents.Email, cancellationToken)).Contains(me);
        return (from topic in Enum.GetValues<Topic>()
                from channel in Enum.GetValues<Channel>()
                let choice = saved.SingleOrDefault(p => p.Topic == topic && p.Channel == channel)
                select new PreferenceDto(topic, channel, choice?.Enabled ?? TopicPreference.Default(topic, channel), channel == Channel.Push ? pushConsent : emailConsent))
            .ToList();
    }

    public async Task<Result> SetPreferenceAsync(SetPreferenceRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.PersonId is not { } me)
        {
            return SignIn;
        }

        var now = clock.GetUtcNow();
        var existing = await db.Preferences.SingleOrDefaultAsync(p => p.PersonId == me && p.Topic == request.Topic && p.Channel == request.Channel, cancellationToken);
        if (existing is null)
        {
            db.Preferences.Add(TopicPreference.Set(me, request.Topic, request.Channel, request.Enabled, now));
        }
        else
        {
            existing.Change(request.Enabled, now);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
