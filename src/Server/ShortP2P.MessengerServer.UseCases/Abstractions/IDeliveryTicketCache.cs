using ShortP2P.MessengerServer.Domain;

namespace ShortP2P.MessengerServer.UseCases.Abstractions;

/// <summary>Hot cache for delivery tickets (receipts).</summary>
public interface IDeliveryTicketCache
{
    Task AddAsync(CachedDeliveryTicket entry, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DeliveryTicket>> ListForSourceNetworkIdAsync(
        string srcNetworkId,
        CancellationToken cancellationToken = default);

    Task RemoveByMessageIdsAsync(
        IReadOnlyCollection<string> messageIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes expired cache entries and returns them for promotion to the durable repository.
    /// </summary>
    Task<IReadOnlyList<CachedDeliveryTicket>> TakeExpiredAsync(
        DateTime olderThanUtc,
        CancellationToken cancellationToken = default);
}
