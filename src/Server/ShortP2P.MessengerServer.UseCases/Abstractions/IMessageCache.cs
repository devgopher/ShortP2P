using ShortP2P.MessengerServer.Domain;

namespace ShortP2P.MessengerServer.UseCases.Abstractions;

/// <summary>Hot cache for store-and-forward messages.</summary>
public interface IMessageCache
{
    Task AddAsync(Message message, CancellationToken cancellationToken = default);

    Task<Message?> FindByIdAsync(string messageId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Message>> ListByTargetNetworkIdAsync(
        string tgtNetworkId,
        CancellationToken cancellationToken = default);

    Task RemoveByIdsAsync(IReadOnlyCollection<string> messageIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes entries whose cache age exceeds <paramref name="olderThanUtc"/> (cachedAt &lt; olderThanUtc)
    /// and returns them so the caller can ensure they exist in the durable repository.
    /// </summary>
    Task<IReadOnlyList<Message>> TakeExpiredAsync(
        DateTime olderThanUtc,
        CancellationToken cancellationToken = default);
}
