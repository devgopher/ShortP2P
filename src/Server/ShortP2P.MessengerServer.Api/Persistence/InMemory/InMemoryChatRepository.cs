using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.Api.Persistence.InMemory;

public sealed class InMemoryChatRepository(InMemoryMessengerStore store) : IChatRepository
{
    public Task<IReadOnlyList<Chat>> ListByNetworkIdAsync(
        string networkId,
        CancellationToken cancellationToken = default)
    {
        var list = store.Chats.Values
            .Where(c => c.NetworkIds.Contains(networkId, StringComparer.Ordinal))
            .ToArray();
        return Task.FromResult<IReadOnlyList<Chat>>(list);
    }

    public Task<Chat?> FindByParticipantsAsync(
        string networkIdA,
        string networkIdB,
        CancellationToken cancellationToken = default)
    {
        var match = store.Chats.Values.FirstOrDefault(c =>
            c.NetworkIds.Count == 2
            && c.NetworkIds.Contains(networkIdA, StringComparer.Ordinal)
            && c.NetworkIds.Contains(networkIdB, StringComparer.Ordinal));
        return Task.FromResult(match);
    }

    public Task AddAsync(Chat chat, CancellationToken cancellationToken = default)
    {
        if (!store.Chats.TryAdd(chat.ChatId, chat))
            throw new InvalidOperationException($"Chat '{chat.ChatId}' already exists.");
        return Task.CompletedTask;
    }
}
