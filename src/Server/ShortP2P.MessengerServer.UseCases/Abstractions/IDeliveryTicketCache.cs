using ShortP2P.MessengerServer.Domain;

namespace ShortP2P.MessengerServer.UseCases.Abstractions;

/// <summary>Hot cache for delivery tickets (receipts).</summary>
public interface IDeliveryTicketCache
{
    /// <summary>Whether the cache can accept new writes (e.g. under memory limit).</summary>
    bool IsWriteAvailable { get; }

    Task AddAsync(CachedDeliveryTicket entry, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DeliveryTicket>> ListForSourceNetworkIdAsync(
        string srcNetworkId,
        CancellationToken cancellationToken = default);

    Task RemoveByMessageIdsAsync(
        IReadOnlyCollection<string> messageIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists expired cache entries without removing them.
    /// </summary>
    Task<IReadOnlyList<CachedDeliveryTicket>> ListExpiredAsync(
        DateTime olderThanUtc,
        CancellationToken cancellationToken = default);
}
