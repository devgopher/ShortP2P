using Microsoft.EntityFrameworkCore;
using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.Persistence.Psql.Entities;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.Persistence.Psql.Repositories;

public sealed class PostgresDeliveryTicketRepository(MessengerDbContext db) : IDeliveryTicketRepository
{
    public async Task AddAsync(DeliveryTicket ticket, CancellationToken cancellationToken = default)
    {
        var exists = await db.DeliveryTickets
            .AnyAsync(x => x.MessageId == ticket.MessageId, cancellationToken)
            .ConfigureAwait(false);
        if (exists)
            return;

        db.DeliveryTickets.Add(DeliveryTicketRecord.FromDomain(ticket));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DeliveryTicket>> ListForSourceNetworkIdAsync(
        string srcNetworkId,
        CancellationToken cancellationToken = default)
    {
        var rows = await (
                from ticket in db.DeliveryTickets.AsNoTracking()
                join message in db.Messages.AsNoTracking() on ticket.MessageId equals message.MessageId
                where message.SrcNetworkId == srcNetworkId
                orderby ticket.ReceivedAtUtc
                select ticket)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(x => x.ToDomain()).ToArray();
    }

    public async Task RemoveByMessageIdsAsync(
        IReadOnlyCollection<string> messageIds,
        CancellationToken cancellationToken = default)
    {
        await db.DeliveryTickets
            .Where(x => messageIds.Contains(x.MessageId))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
