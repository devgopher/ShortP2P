using ShortP2P.MessengerServer.Domain;

namespace ShortP2P.MessengerServer.UseCases.Abstractions;

public interface IChatRequestRepository
{
    Task AddAsync(ChatRequest request, CancellationToken cancellationToken = default);

    Task AddInboxAsync(ChatRequestInboxEntry entry, CancellationToken cancellationToken = default);

    Task<ChatRequest?> FindByIdAsync(string requestId, CancellationToken cancellationToken = default);

    Task<bool> InboxExistsAsync(
        string requestId,
        string deviceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns pending chat requests for the device and removes those inbox rows (consume-on-read per device).
    /// Deletes the parent request when no inbox rows remain.
    /// </summary>
    Task<IReadOnlyList<ChatRequest>> TakeForDeviceAsync(
        string targetNetworkId,
        string deviceId,
        CancellationToken cancellationToken = default);

    /// <summary>All chat requests addressed to target (for lazy fan-out).</summary>
    Task<IReadOnlyList<ChatRequest>> ListByTargetNetworkIdAsync(
        string targetNetworkId,
        CancellationToken cancellationToken = default);

    Task RemoveOlderThanAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default);
}
