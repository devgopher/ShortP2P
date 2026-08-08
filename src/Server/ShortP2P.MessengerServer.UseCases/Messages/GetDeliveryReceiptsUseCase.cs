using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.UseCases.Messages;

public sealed class GetDeliveryReceiptsUseCase(
    IDeliveryTicketRepository tickets,
    IDeliveryTicketCache ticketCache)
{
    public async Task<IReadOnlyList<DeliveryTicket>> ExecuteAsync(
        GetDeliveryReceiptsQuery query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query.CallerNetworkId))
            throw UseCaseException.Validation("CallerNetworkId is required.");

        var caller = query.CallerNetworkId.Trim();

        var cached = await ticketCache.ListForSourceNetworkIdAsync(caller, cancellationToken)
            .ConfigureAwait(false);
        var result = cached.Count > 0
            ? cached
            : await tickets.ListForSourceNetworkIdAsync(caller, cancellationToken).ConfigureAwait(false);

        if (result.Count == 0)
            return result;

        var ids = result.Select(t => t.MessageId).ToArray();
        await Task.WhenAll(
            ticketCache.RemoveByMessageIdsAsync(ids, cancellationToken),
            tickets.RemoveByMessageIdsAsync(ids, cancellationToken)).ConfigureAwait(false);

        return result;
    }
}
