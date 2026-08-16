using System.Collections.Concurrent;
using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.Infrastructure.Caching;

public sealed class InMemoryMessageInboxCache(InMemoryCacheMemoryTracker memory) : IMessageInboxCache
{
    private readonly ConcurrentDictionary<(string MessageId, string DeviceId), MessageInboxEntry> _entries = new();

    public bool IsWriteAvailable => memory.IsWriteAvailable;

    public Task AddAsync(MessageInboxEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();

        const long size = 128;
        if (!memory.TryReserve(size))
            throw new InvalidOperationException("In-memory message inbox cache memory limit exceeded.");

        if (!_entries.TryAdd((entry.MessageId, entry.DeviceId), entry))
            memory.Release(size);

        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(
        string messageId,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_entries.ContainsKey((messageId, deviceId)));
    }

    public Task<IReadOnlyList<string>> ListMessageIdsForDeviceAsync(
        string tgtNetworkId,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<string> ids = _entries.Values
            .Where(e =>
                string.Equals(e.TgtNetworkId, tgtNetworkId, StringComparison.Ordinal) &&
                string.Equals(e.DeviceId, deviceId, StringComparison.Ordinal))
            .Select(e => e.MessageId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return Task.FromResult(ids);
    }

    public Task RemoveAsync(
        string messageId,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_entries.TryRemove((messageId, deviceId), out _))
            memory.Release(128);
        return Task.CompletedTask;
    }

    public Task<int> CountForMessageAsync(string messageId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_entries.Keys.Count(k => k.MessageId == messageId));
    }

    public Task RemoveAllForMessageAsync(string messageId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var key in _entries.Keys.Where(k => k.MessageId == messageId).ToArray())
        {
            if (_entries.TryRemove(key, out _))
                memory.Release(128);
        }

        return Task.CompletedTask;
    }
}
