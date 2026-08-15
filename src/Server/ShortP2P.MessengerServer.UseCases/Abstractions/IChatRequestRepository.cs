using ShortP2P.MessengerServer.Domain;

namespace ShortP2P.MessengerServer.UseCases.Abstractions;

public interface IChatRequestRepository
{
    Task AddAsync(ChatRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChatRequest>> ListByTargetNetworkIdAsync(
        string targetNetworkId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns pending requests for <paramref name="targetNetworkId"/> and removes them
    /// (accepted / delivered to the client) from durable store / in-memory cache.
    /// </summary>
    Task<IReadOnlyList<ChatRequest>> TakeByTargetNetworkIdAsync(
        string targetNetworkId,
        CancellationToken cancellationToken = default);
}
