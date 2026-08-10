using Microsoft.EntityFrameworkCore;
using ShortP2P.MessengerServer.Auth.EntityFramework.Entities;
using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.Auth.EntityFramework.Repositories;

public sealed class EfClientAccountRepository(AuthDbContext db) : IClientAccountRepository
{
    public async Task<ClientAccount?> FindByNetworkIdAsync(
        string networkId,
        CancellationToken cancellationToken = default)
    {
        var row = await db.Accounts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.NetworkId == networkId, cancellationToken)
            .ConfigureAwait(false);
        return row?.ToDomain();
    }

    public async Task<ClientAccount?> FindByNickAsync(string nick, CancellationToken cancellationToken = default)
    {
        var row = await db.Accounts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Nick == nick, cancellationToken)
            .ConfigureAwait(false);
        return row?.ToDomain();
    }

    public async Task AddAsync(ClientAccount account, CancellationToken cancellationToken = default)
    {
        db.Accounts.Add(AuthAccountEntity.FromDomain(account));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
