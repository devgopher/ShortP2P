using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.Api.Persistence.InMemory;

public sealed class InMemoryChatRequestRepository(InMemoryMessengerStore store) : IChatRequestRepository
{
    public Task AddAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        store.ChatRequests[request.RequestId] = request;
        return Task.CompletedTask;
    }

    public Task AddInboxAsync(ChatRequestInboxEntry entry, CancellationToken cancellationToken = default)
    {
        store.ChatRequestInboxes.TryAdd((entry.RequestId, entry.DeviceId), entry);
        return Task.CompletedTask;
    }

    public Task<ChatRequest?> FindByIdAsync(string requestId, CancellationToken cancellationToken = default)
    {
        store.ChatRequests.TryGetValue(requestId, out var request);
        return Task.FromResult(request);
    }

    public Task<bool> InboxExistsAsync(
        string requestId,
        string deviceId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(store.ChatRequestInboxes.ContainsKey((requestId, deviceId)));

    public Task<IReadOnlyList<ChatRequest>> TakeForDeviceAsync(
        string targetNetworkId,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        var inboxKeys = store.ChatRequestInboxes
            .Where(kv =>
                string.Equals(kv.Value.TargetNetworkId, targetNetworkId, StringComparison.Ordinal) &&
                string.Equals(kv.Value.DeviceId, deviceId, StringComparison.Ordinal))
            .Select(kv => kv.Key)
            .ToArray();

        if (inboxKeys.Length == 0)
            return Task.FromResult<IReadOnlyList<ChatRequest>>([]);

        var requests = new List<ChatRequest>();
        foreach (var key in inboxKeys)
        {
            store.ChatRequestInboxes.TryRemove(key, out _);
            if (store.ChatRequests.TryGetValue(key.RequestId, out var request))
                requests.Add(request);
        }

        foreach (var requestId in inboxKeys.Select(k => k.RequestId).Distinct(StringComparer.Ordinal))
        {
            if (!store.ChatRequestInboxes.Keys.Any(k => k.RequestId == requestId))
                store.ChatRequests.TryRemove(requestId, out _);
        }

        return Task.FromResult<IReadOnlyList<ChatRequest>>(
            requests.OrderBy(r => r.CreatedAtUtc).ToArray());
    }

    public Task<IReadOnlyList<ChatRequest>> ListByTargetNetworkIdAsync(
        string targetNetworkId,
        CancellationToken cancellationToken = default)
    {
        var list = store.ChatRequests.Values
            .Where(r => string.Equals(r.TargetNetworkId, targetNetworkId, StringComparison.Ordinal))
            .OrderBy(r => r.CreatedAtUtc)
            .ToArray();
        return Task.FromResult<IReadOnlyList<ChatRequest>>(list);
    }

    public Task RemoveOlderThanAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default)
    {
        var ids = store.ChatRequests.Values
            .Where(r => r.CreatedAtUtc < cutoffUtc)
            .Select(r => r.RequestId)
            .ToArray();

        foreach (var id in ids)
        {
            store.ChatRequests.TryRemove(id, out _);
            foreach (var key in store.ChatRequestInboxes.Keys.Where(k => k.RequestId == id).ToArray())
                store.ChatRequestInboxes.TryRemove(key, out _);
        }

        return Task.CompletedTask;
    }
}
