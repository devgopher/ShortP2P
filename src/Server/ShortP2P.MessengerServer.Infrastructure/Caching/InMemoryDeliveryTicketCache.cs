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

    public Task<IReadOnlyList<CachedDeliveryTicket>> ListExpiredAsync(
        DateTime olderThanUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<CachedDeliveryTicket> expired = _entries.Values
            .Where(e => e.CachedAtUtc < olderThanUtc)
            .Select(e => e.Value)
            .ToArray();
        return Task.FromResult(expired);
    }

    private sealed record CacheEntry(CachedDeliveryTicket Value, DateTime CachedAtUtc, long SizeBytes);
}
