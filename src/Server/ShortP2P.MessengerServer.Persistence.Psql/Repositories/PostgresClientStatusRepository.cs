using Microsoft.EntityFrameworkCore;
using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.Persistence.Psql.Entities;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.Persistence.Psql.Repositories;

public sealed class PostgresClientStatusRepository(MessengerDbContext db) : IClientStatusRepository
{
    public async Task UpsertAsync(ClientStatuses status, CancellationToken cancellationToken = default)
    {
        var existing = await db.ClientStatuses
            .FirstOrDefaultAsync(
                x => x.NetworkId == status.NetworkId && x.DeviceId == status.DeviceId,
                cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            db.ClientStatuses.Add(ClientStatusRecord.FromDomain(status));
        }
        else
        {
            existing.Status = status.Status;
            existing.CreatedAtUtc = status.CreatedAtUtc;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ClientStatuses>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        var rows = await db.ClientStatuses
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(x => x.ToDomain()).ToArray();
    }

    public async Task<IReadOnlyList<string>> ListDeviceIdsAsync(
        string networkId,
        CancellationToken cancellationToken = default)
    {
        var ids = await db.ClientStatuses.AsNoTracking()
            .Where(x => x.NetworkId == networkId)
            .Select(x => x.DeviceId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return ids;
    }
}
