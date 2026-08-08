using ShortP2P.MessengerServer.Domain;

namespace ShortP2P.MessengerServer.UseCases.Abstractions;

public interface IChatRequestRepository
{
    Task AddAsync(ChatRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChatRequest>> ListByTargetNetworkIdAsync(
        string targetNetworkId,
        CancellationToken cancellationToken = default);
}
