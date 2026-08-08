namespace ShortP2P.MessengerServer.Infrastructure.Caching;

/// <summary>Options for in-memory message/ticket caches.</summary>
public sealed class InMemoryMessengerCacheOptions
{
    /// <summary>
    /// Shared memory limit for in-memory caches, in megabytes.
    /// Null or &lt;= 0 means unlimited (default).
    /// </summary>
    public int? MaxMemoryMegabytes { get; set; }
}
