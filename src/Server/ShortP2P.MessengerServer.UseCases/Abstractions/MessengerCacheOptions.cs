namespace ShortP2P.MessengerServer.UseCases.Abstractions;

/// <summary>Storage settings for message/ticket cache and durable repository.</summary>
public sealed class MessengerCacheOptions
{
    /// <summary>When false, cache is not used (reads/writes go to repository only if enabled).</summary>
    public bool CacheEnabled { get; set; } = true;

    /// <summary>When false, durable repository is not used (reads/writes go to cache only if enabled).</summary>
    public bool RepositoryEnabled { get; set; } = true;

    /// <summary>How long an item may stay in cache before promotion to the durable repository. Default: 60 seconds.</summary>
    public TimeSpan TimeToLive { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>How often the background job checks for expired cache entries. Default: 10 seconds.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(10);
}
