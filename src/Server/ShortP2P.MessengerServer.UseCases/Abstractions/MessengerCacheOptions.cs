namespace ShortP2P.MessengerServer.UseCases.Abstractions;

/// <summary>Cache TTL settings for messages and delivery tickets.</summary>
public sealed class MessengerCacheOptions
{
    /// <summary>How long an item may stay in cache before promotion to the durable repository. Default: 60 seconds.</summary>
    public TimeSpan TimeToLive { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>How often the background job checks for expired cache entries. Default: 10 seconds.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(10);
}
