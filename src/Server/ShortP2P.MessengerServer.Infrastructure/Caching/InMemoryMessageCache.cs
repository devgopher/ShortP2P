using System.Collections.Concurrent;
using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.Infrastructure.Caching;

public sealed class InMemoryMessageCache(InMemoryCacheMemoryTracker memory, IClock clock) : IMessageCache
{
    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);

    public bool IsWriteAvailable => memory.IsWriteAvailable;

    public Task AddAsync(Message message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();

        var size = InMemoryCacheSizeEstimator.Estimate(message);
        if (!memory.TryReserve(size))
            throw new InvalidOperationException("In-memory message cache memory limit exceeded.");

        var entry = new CacheEntry(message, clock.UtcNow, size);
        if (!_entries.TryAdd(message.MessageId, entry))
        {
            memory.Release(size);
            return Task.CompletedTask; // idempotent: already cached
        }

        return Task.CompletedTask;
    }

    public Task<Message?> FindByIdAsync(string messageId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_entries.TryGetValue(messageId, out var entry) ? entry.Message : null);
    }

    public Task<IReadOnlyList<Message>> ListByTargetNetworkIdAsync(
        string tgtNetworkId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<Message> list = _entries.Values
            .Where(e => string.Equals(e.Message.TgtNetworkId, tgtNetworkId, StringComparison.Ordinal))
            .Select(e => e.Message)
            .ToArray();
        return Task.FromResult(list);
    }

    public Task RemoveByIdsAsync(IReadOnlyCollection<string> messageIds, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var id in messageIds)
        {
            if (_entries.TryRemove(id, out var entry))
                memory.Release(entry.SizeBytes);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Message>> ListExpiredAsync(
        DateTime olderThanUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<Message> expired = _entries.Values
            .Where(e => e.CachedAtUtc < olderThanUtc)
            .Select(e => e.Message)
            .ToArray();
        return Task.FromResult(expired);
    }

    private sealed record CacheEntry(Message Message, DateTime CachedAtUtc, long SizeBytes);
}
