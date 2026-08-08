using ShortP2P.MessengerServer.Domain;

namespace ShortP2P.MessengerServer.UseCases.Abstractions;

public interface IChatRepository
{
    Task<IReadOnlyList<Chat>> ListByNetworkIdAsync(string networkId, CancellationToken cancellationToken = default);

    /// <summary>Returns an existing chat for the exact participant pair, if any.</summary>
    Task<Chat?> FindByParticipantsAsync(
        string networkIdA,
        string networkIdB,
        CancellationToken cancellationToken = default);

    Task AddAsync(Chat chat, CancellationToken cancellationToken = default);
}
