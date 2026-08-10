using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.Api.Persistence.InMemory;

public sealed class InMemoryDeliveryTicketRepository(InMemoryMessengerStore store) : IDeliveryTicketRepository
{
    public Task AddAsync(DeliveryTicket ticket, CancellationToken cancellationToken = default)
    {
        var src = store.Messages.TryGetValue(ticket.MessageId, out var message)
            ? message.SrcNetworkId
            : string.Empty;

        store.DeliveryTickets.TryAdd(ticket.MessageId, (ticket, src));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DeliveryTicket>> ListForSourceNetworkIdAsync(
        string srcNetworkId,
        CancellationToken cancellationToken = default)
    {
        var list = store.DeliveryTickets.Values
            .Where(x => string.Equals(x.SrcNetworkId, srcNetworkId, StringComparison.Ordinal))
            .Select(x => x.Ticket)
            .OrderBy(t => t.ReceivedAtUtc)
            .ToArray();
        return Task.FromResult<IReadOnlyList<DeliveryTicket>>(list);
    }

    public Task RemoveByMessageIdsAsync(
        IReadOnlyCollection<string> messageIds,
        CancellationToken cancellationToken = default)
    {
        foreach (var id in messageIds)
            store.DeliveryTickets.TryRemove(id, out _);
        return Task.CompletedTask;
    }
}
