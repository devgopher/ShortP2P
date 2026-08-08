using Microsoft.EntityFrameworkCore;
using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.Persistence.Psql.Entities;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.Persistence.Psql.Repositories;

public sealed class PostgresChatRepository(MessengerDbContext db) : IChatRepository
{
    public async Task<IReadOnlyList<Chat>> ListByNetworkIdAsync(
        string networkId,
        CancellationToken cancellationToken = default)
    {
        var rows = await db.Chats.AsNoTracking()
            .Where(x => x.NetworkIds.Contains(networkId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(x => x.ToDomain()).ToArray();
    }

    public async Task<Chat?> FindByParticipantsAsync(
        string networkIdA,
        string networkIdB,
        CancellationToken cancellationToken = default)
    {
        var rows = await db.Chats.AsNoTracking()
            .Where(x => x.NetworkIds.Contains(networkIdA) && x.NetworkIds.Contains(networkIdB))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var match = rows.FirstOrDefault(x =>
            x.NetworkIds.Count == 2
            && x.NetworkIds.Contains(networkIdA)
            && x.NetworkIds.Contains(networkIdB));

        return match?.ToDomain();
    }

    public async Task AddAsync(Chat chat, CancellationToken cancellationToken = default)
    {
        db.Chats.Add(ChatRecord.FromDomain(chat));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
