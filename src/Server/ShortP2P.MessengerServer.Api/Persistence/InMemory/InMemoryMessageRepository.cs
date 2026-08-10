using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.Api.Persistence.InMemory;

public sealed class InMemoryMessageRepository(InMemoryMessengerStore store) : IMessageRepository
{
    public Task<Message?> FindByIdAsync(string messageId, CancellationToken cancellationToken = default)
    {
        store.Messages.TryGetValue(messageId, out var message);
        return Task.FromResult(message);
    }

    public Task AddAsync(Message message, CancellationToken cancellationToken = default)
    {
        store.Messages.TryAdd(message.MessageId, message);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Message>> ListByTargetNetworkIdAsync(
        string tgtNetworkId,
        CancellationToken cancellationToken = default)
    {
        var list = store.Messages.Values
            .Where(m => string.Equals(m.TgtNetworkId, tgtNetworkId, StringComparison.Ordinal))
            .OrderBy(m => m.CreatedUtc)
            .ToArray();
        return Task.FromResult<IReadOnlyList<Message>>(list);
    }

    public Task RemoveByIdsAsync(IReadOnlyCollection<string> messageIds, CancellationToken cancellationToken = default)
    {
        foreach (var id in messageIds)
            store.Messages.TryRemove(id, out _);
        return Task.CompletedTask;
    }
}
