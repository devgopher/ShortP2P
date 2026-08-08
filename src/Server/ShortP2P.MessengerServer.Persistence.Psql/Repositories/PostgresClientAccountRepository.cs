using Microsoft.EntityFrameworkCore;
using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.Persistence.Psql.Entities;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.Persistence.Psql.Repositories;

public sealed class PostgresClientAccountRepository(MessengerDbContext db) : IClientAccountRepository
{
    public async Task<ClientAccount?> FindByNetworkIdAsync(
        string networkId,
        CancellationToken cancellationToken = default)
    {
        var row = await db.ClientAccounts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.NetworkId == networkId, cancellationToken)
            .ConfigureAwait(false);
        return row?.ToDomain();
    }

    public async Task<ClientAccount?> FindByNickAsync(string nick, CancellationToken cancellationToken = default)
    {
        var row = await db.ClientAccounts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Nick == nick, cancellationToken)
            .ConfigureAwait(false);
        return row?.ToDomain();
    }

    public async Task AddAsync(ClientAccount account, CancellationToken cancellationToken = default)
    {
        db.ClientAccounts.Add(ClientAccountRecord.FromDomain(account));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
