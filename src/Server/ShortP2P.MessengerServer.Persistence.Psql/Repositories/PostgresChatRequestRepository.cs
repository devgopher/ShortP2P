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

    public async Task AddInboxAsync(ChatRequestInboxEntry entry, CancellationToken cancellationToken = default)
    {
        var exists = await db.ChatRequestInboxes
            .AnyAsync(x => x.RequestId == entry.RequestId && x.DeviceId == entry.DeviceId, cancellationToken)
            .ConfigureAwait(false);
        if (exists)
            return;

        db.ChatRequestInboxes.Add(new ChatRequestInboxRecord
        {
            RequestId = entry.RequestId,
            TargetNetworkId = entry.TargetNetworkId,
            DeviceId = entry.DeviceId
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ChatRequest?> FindByIdAsync(string requestId, CancellationToken cancellationToken = default)
    {
        var row = await db.ChatRequests.AsNoTracking()
            .FirstOrDefaultAsync(x => x.RequestId == requestId, cancellationToken)
            .ConfigureAwait(false);
        return row?.ToDomain();
    }

    public Task<bool> InboxExistsAsync(
        string requestId,
        string deviceId,
        CancellationToken cancellationToken = default) =>
        db.ChatRequestInboxes.AsNoTracking()
            .AnyAsync(x => x.RequestId == requestId && x.DeviceId == deviceId, cancellationToken);

    public async Task<IReadOnlyList<ChatRequest>> TakeForDeviceAsync(
        string targetNetworkId,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        var inboxRows = await db.ChatRequestInboxes
            .Where(x => x.TargetNetworkId == targetNetworkId && x.DeviceId == deviceId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (inboxRows.Count == 0)
            return [];

        var requestIds = inboxRows.Select(x => x.RequestId).Distinct().ToArray();
        var requests = await db.ChatRequests.AsNoTracking()
            .Where(x => requestIds.Contains(x.RequestId))
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        db.ChatRequestInboxes.RemoveRange(inboxRows);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        foreach (var requestId in requestIds)
        {
            var remaining = await db.ChatRequestInboxes
                .CountAsync(x => x.RequestId == requestId, cancellationToken)
                .ConfigureAwait(false);
            if (remaining == 0)
            {
                await db.ChatRequests
                    .Where(x => x.RequestId == requestId)
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        return requests.Select(x => x.ToDomain()).ToArray();
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

    public async Task RemoveOlderThanAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default)
    {
        var ids = await db.ChatRequests.AsNoTracking()
            .Where(x => x.CreatedAtUtc < cutoffUtc)
            .Select(x => x.RequestId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (ids.Count == 0)
            return;

        await db.ChatRequestInboxes
            .Where(x => ids.Contains(x.RequestId))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await db.ChatRequests
            .Where(x => ids.Contains(x.RequestId))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
