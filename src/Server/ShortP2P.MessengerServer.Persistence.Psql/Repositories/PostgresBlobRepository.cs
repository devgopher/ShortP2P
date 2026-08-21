using Microsoft.EntityFrameworkCore;
using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.Persistence.Psql.Entities;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.Persistence.Psql.Repositories;

public sealed class PostgresBlobRepository(MessengerDbContext db) : IBlobRepository
{
    public async Task<Blob?> FindByIdAsync(string blobId, CancellationToken cancellationToken = default)
    {
        var row = await db.Blobs.AsNoTracking()
            .FirstOrDefaultAsync(x => x.BlobId == blobId, cancellationToken)
            .ConfigureAwait(false);
        return row?.ToDomain();
    }

    public async Task AddAsync(Blob blob, CancellationToken cancellationToken = default)
    {
        db.Blobs.Add(BlobRecord.FromDomain(blob));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveOlderThanAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default)
    {
        await db.Blobs
            .Where(x => x.CreatedUtc < cutoffUtc)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
