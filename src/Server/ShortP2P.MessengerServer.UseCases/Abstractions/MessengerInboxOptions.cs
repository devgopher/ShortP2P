namespace ShortP2P.MessengerServer.UseCases.Abstractions;

/// <summary>Long-poll and retention settings for the messenger inbox.</summary>
public sealed class MessengerInboxOptions
{
    public const string Section = "MessengerInbox";

    /// <summary>Maximum long-poll wait in seconds (also default when query omits timeout).</summary>
    public int MaxPollTimeoutSeconds { get; set; } = 30;

    /// <summary>How long undelivered messages / chat requests may remain on the server.</summary>
    public TimeSpan MessageRetention { get; set; } = TimeSpan.FromDays(30);

    /// <summary>Online if last device touch is within this window (1.5 × max poll).</summary>
    public TimeSpan OnlineTimeout => TimeSpan.FromSeconds(MaxPollTimeoutSeconds * 1.5);
}
