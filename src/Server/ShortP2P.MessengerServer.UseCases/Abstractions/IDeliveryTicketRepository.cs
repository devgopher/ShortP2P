using ShortP2P.MessengerServer.Domain;

namespace ShortP2P.MessengerServer.UseCases.Abstractions;

public interface IDeliveryTicketRepository
{
    Task AddAsync(DeliveryTicket ticket, CancellationToken cancellationToken = default);

    /// <summary>Receipts for messages sent by the given source network id.</summary>
    Task<IReadOnlyList<DeliveryTicket>> ListForSourceNetworkIdAsync(
        string srcNetworkId,
        CancellationToken cancellationToken = default);

    Task RemoveByMessageIdsAsync(
        IReadOnlyCollection<string> messageIds,
        CancellationToken cancellationToken = default);
}
