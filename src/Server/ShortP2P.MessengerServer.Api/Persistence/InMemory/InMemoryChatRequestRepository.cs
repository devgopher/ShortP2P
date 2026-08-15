using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.Api.Persistence.InMemory;

public sealed class InMemoryChatRequestRepository(InMemoryMessengerStore store) : IChatRequestRepository
{
    public Task AddAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        store.AddChatRequest(request);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ChatRequest>> ListByTargetNetworkIdAsync(
        string targetNetworkId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(store.ListChatRequestsByTarget(targetNetworkId));

    public Task<IReadOnlyList<ChatRequest>> TakeByTargetNetworkIdAsync(
        string targetNetworkId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(store.TakeChatRequestsByTarget(targetNetworkId));
}
