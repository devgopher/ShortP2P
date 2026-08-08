using System.Collections.Concurrent;
using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.Infrastructure.Caching;

public sealed class InMemoryDeliveryTicketCache(InMemoryCacheMemoryTracker memory, IClock clock)
    : IDeliveryTicketCache
{
    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);

    public bool IsWriteAvailable => memory.IsWriteAvailable;

    public Task AddAsync(CachedDeliveryTicket entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();

        var size = InMemoryCacheSizeEstimator.Estimate(entry);
        if (!memory.TryReserve(size))
            throw new InvalidOperationException("In-memory delivery ticket cache memory limit exceeded.");

        var cacheEntry = new CacheEntry(entry, clock.UtcNow, size);
        if (!_entries.TryAdd(entry.Ticket.MessageId, cacheEntry))
        {
            memory.Release(size);
            return Task.CompletedTask;
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DeliveryTicket>> ListForSourceNetworkIdAsync(
        string srcNetworkId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<DeliveryTicket> list = _entries.Values
            .Where(e => string.Equals(e.Value.SrcNetworkId, srcNetworkId, StringComparison.Ordinal))
            .Select(e => e.Value.Ticket)
            .ToArray();
        return Task.FromResult(list);
    }

    public Task RemoveByMessageIdsAsync(
        IReadOnlyCollection<string> messageIds,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var id in messageIds)
        {
            if (_entries.TryRemove(id, out var entry))
                memory.Release(entry.SizeBytes);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CachedDeliveryTicket>> TakeExpiredAsync(
        DateTime olderThanUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var expired = new List<CachedDeliveryTicket>();
        foreach (var pair in _entries)
        {
            if (pair.Value.CachedAtUtc >= olderThanUtc)
                continue;

            if (!_entries.TryRemove(pair.Key, out var entry)) 
                continue;
            memory.Release(entry.SizeBytes);
            expired.Add(entry.Value);
        }

        return Task.FromResult<IReadOnlyList<CachedDeliveryTicket>>(expired);
    }

    private sealed record CacheEntry(CachedDeliveryTicket Value, DateTime CachedAtUtc, long SizeBytes);
}
