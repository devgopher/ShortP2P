using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.Api.Persistence.InMemory;

public sealed class InMemoryMessageInboxRepository(InMemoryMessengerStore store) : IMessageInboxRepository
{
    public Task AddAsync(MessageInboxEntry entry, CancellationToken cancellationToken = default)
    {
        store.MessageInboxes.TryAdd((entry.MessageId, entry.DeviceId), entry);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(
        string messageId,
        string deviceId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(store.MessageInboxes.ContainsKey((messageId, deviceId)));

    public Task<IReadOnlyList<Message>> ListMessagesForDeviceAsync(
        string tgtNetworkId,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        var list = store.MessageInboxes.Values
            .Where(e =>
                string.Equals(e.TgtNetworkId, tgtNetworkId, StringComparison.Ordinal) &&
                string.Equals(e.DeviceId, deviceId, StringComparison.Ordinal))
            .Select(e => store.Messages.TryGetValue(e.MessageId, out var m) ? m : null)
            .Where(m => m is not null)
            .Cast<Message>()
            .OrderBy(m => m.CreatedUtc)
            .ToArray();
        return Task.FromResult<IReadOnlyList<Message>>(list);
    }

    public Task RemoveAsync(
        string messageId,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        store.MessageInboxes.TryRemove((messageId, deviceId), out _);
        return Task.CompletedTask;
    }

    public Task<int> CountForMessageAsync(string messageId, CancellationToken cancellationToken = default) =>
        Task.FromResult(store.MessageInboxes.Keys.Count(k => k.MessageId == messageId));

    public Task RemoveAllForMessageAsync(string messageId, CancellationToken cancellationToken = default)
    {
        foreach (var key in store.MessageInboxes.Keys.Where(k => k.MessageId == messageId).ToArray())
            store.MessageInboxes.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}
