using Microsoft.EntityFrameworkCore;
using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.Persistence.Psql.Entities;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.Persistence.Psql.Repositories;

public sealed class PostgresChatRequestRepository(MessengerDbContext db) : IChatRequestRepository
{
    public async Task AddAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        db.ChatRequests.Add(ChatRequestRecord.FromDomain(request));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ChatRequest>> ListByTargetNetworkIdAsync(
        string targetNetworkId,
        CancellationToken cancellationToken = default)
    {
        var rows = await db.ChatRequests.AsNoTracking()
            .Where(x => x.TargetNetworkId == targetNetworkId)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(x => x.ToDomain()).ToArray();
    }

    public async Task<IReadOnlyList<ChatRequest>> TakeByTargetNetworkIdAsync(
        string targetNetworkId,
        CancellationToken cancellationToken = default)
    {
        var rows = await db.ChatRequests
            .Where(x => x.TargetNetworkId == targetNetworkId)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (rows.Count == 0)
            return [];

        var result = rows.Select(x => x.ToDomain()).ToArray();
        db.ChatRequests.RemoveRange(rows);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }
}
