namespace Shapers.Media.Domain;

/// <summary>
/// The church's YouTube channel, connected once by its owner so captions can be read. YouTube only lets a channel's
/// owner download its captions. The refresh token is stored encrypted and is the only credential kept.
/// </summary>
public sealed class YouTubeConnection : AggregateRoot<Guid>
{
    private YouTubeConnection()
    {
    }

    public string ChannelId { get; private set; } = null!;

    public string ChannelTitle { get; private set; } = null!;

    /// <summary>Encrypted with the API's data protection keys; never returned by the API.</summary>
    public string ProtectedRefreshToken { get; private set; } = null!;

    public Guid ConnectedByUserId { get; private set; }

    public DateTimeOffset ConnectedAt { get; private set; }

    public static YouTubeConnection Connect(string channelId, string channelTitle, string protectedRefreshToken, Guid userId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(channelId) || string.IsNullOrWhiteSpace(protectedRefreshToken))
        {
            throw new DomainRuleException("media.youtube_incomplete", "YouTube didn't give us access to a channel. Try connecting again.");
        }

        return new YouTubeConnection
        {
            Id = Guid.CreateVersion7(),
            ChannelId = channelId,
            ChannelTitle = string.IsNullOrWhiteSpace(channelTitle) ? channelId : channelTitle.Trim(),
            ProtectedRefreshToken = protectedRefreshToken,
            ConnectedByUserId = userId,
            ConnectedAt = now,
        };
    }
}
