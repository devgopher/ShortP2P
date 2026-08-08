using ShortP2P.MessengerServer.Domain;

namespace ShortP2P.MessengerServer.UseCases.Abstractions;

/// <summary>Hot cache for store-and-forward messages.</summary>
public interface IMessageCache
{
    /// <summary>Whether the cache can accept new writes (e.g. under memory limit).</summary>
    bool IsWriteAvailable { get; }

    Task AddAsync(Message message, CancellationToken cancellationToken = default);

    Task<Message?> FindByIdAsync(string messageId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Message>> ListByTargetNetworkIdAsync(
        string tgtNetworkId,
        CancellationToken cancellationToken = default);

    Task RemoveByIdsAsync(IReadOnlyCollection<string> messageIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists entries whose cache age exceeds <paramref name="olderThanUtc"/> (cachedAt &lt; olderThanUtc)
    /// without removing them.
    /// </summary>
    Task<IReadOnlyList<Message>> ListExpiredAsync(
        DateTime olderThanUtc,
        CancellationToken cancellationToken = default);
}
