namespace ShortP2P.MessengerServer.Infrastructure.Caching;

/// <summary>Tracks approximate memory usage across in-memory messenger caches.</summary>
public sealed class InMemoryCacheMemoryTracker
{
    private long _usedBytes;
    private readonly long _maxBytes;

    public InMemoryCacheMemoryTracker(InMemoryMessengerCacheOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _maxBytes = options.MaxMemoryMegabytes is > 0
            ? options.MaxMemoryMegabytes.Value * 1024L * 1024L
            : 0;
    }

    public long UsedBytes => Interlocked.Read(ref _usedBytes);

    public long MaxBytes => _maxBytes;

    /// <summary>True when unlimited or current usage is below the configured limit.</summary>
    public bool IsWriteAvailable => _maxBytes <= 0 || UsedBytes < _maxBytes;

    public bool TryReserve(long sizeBytes)
    {
        if (sizeBytes < 0)
            sizeBytes = 0;

        if (_maxBytes <= 0)
        {
            Interlocked.Add(ref _usedBytes, sizeBytes);
            return true;
        }

        while (true)
        {
            var current = Interlocked.Read(ref _usedBytes);
            var next = current + sizeBytes;
            if (next > _maxBytes)
                return false;

            if (Interlocked.CompareExchange(ref _usedBytes, next, current) == current)
                return true;
        }
    }

    public void Release(long sizeBytes)
    {
        if (sizeBytes <= 0)
            return;

        Interlocked.Add(ref _usedBytes, -sizeBytes);
    }
}
