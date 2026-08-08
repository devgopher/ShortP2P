using Microsoft.EntityFrameworkCore;
using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.Persistence.Psql.Entities;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.Persistence.Psql.Repositories;

public sealed class PostgresMessageRepository(MessengerDbContext db) : IMessageRepository
{
    public async Task<Message?> FindByIdAsync(string messageId, CancellationToken cancellationToken = default)
    {
        var row = await db.Messages.AsNoTracking()
            .FirstOrDefaultAsync(x => x.MessageId == messageId, cancellationToken)
            .ConfigureAwait(false);
        return row?.ToDomain();
    }

    public async Task AddAsync(Message message, CancellationToken cancellationToken = default)
    {
        db.Messages.Add(MessageRecord.FromDomain(message));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Message>> ListByTargetNetworkIdAsync(
        string tgtNetworkId,
        CancellationToken cancellationToken = default)
    {
        var rows = await db.Messages.AsNoTracking()
            .Where(x => x.TgtNetworkId == tgtNetworkId)
            .OrderBy(x => x.CreatedUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(x => x.ToDomain()).ToArray();
    }

    public async Task RemoveByIdsAsync(
        IReadOnlyCollection<string> messageIds,
        CancellationToken cancellationToken = default)
    {
        await db.Messages
            .Where(x => messageIds.Contains(x.MessageId))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
