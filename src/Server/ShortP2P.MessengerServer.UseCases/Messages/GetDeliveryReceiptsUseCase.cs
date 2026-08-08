using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.UseCases.Messages;

public sealed class GetDeliveryReceiptsUseCase(
    IDeliveryTicketRepository tickets,
    IDeliveryTicketCache ticketCache,
    MessengerCacheOptions options)
{
    public async Task<IReadOnlyList<DeliveryTicket>> ExecuteAsync(
        GetDeliveryReceiptsQuery query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query.CallerNetworkId))
            throw UseCaseException.Validation("CallerNetworkId is required.");

        StorageAccess.EnsureAnyStoreEnabled(options);

        var caller = query.CallerNetworkId.Trim();
        IReadOnlyList<DeliveryTicket> result = Array.Empty<DeliveryTicket>();

        if (options.CacheEnabled)
        {
            result = await StorageAccess
                .TryListAsync(() => ticketCache.ListForSourceNetworkIdAsync(caller, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
        }

        if (result.Count == 0 && options.RepositoryEnabled)
        {
            result = await StorageAccess
                .TryListAsync(() => tickets.ListForSourceNetworkIdAsync(caller, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
        }

        if (result.Count == 0)
            return result;

        var ids = result.Select(t => t.MessageId).ToArray();
        var removals = new List<Task>(2);
        if (options.CacheEnabled)
            removals.Add(StorageAccess.TryExecuteAsync(() => ticketCache.RemoveByMessageIdsAsync(ids, cancellationToken), cancellationToken));
        if (options.RepositoryEnabled)
            removals.Add(StorageAccess.TryExecuteAsync(() => tickets.RemoveByMessageIdsAsync(ids, cancellationToken), cancellationToken));
        await Task.WhenAll(removals).ConfigureAwait(false);

        return result;
    }
}
