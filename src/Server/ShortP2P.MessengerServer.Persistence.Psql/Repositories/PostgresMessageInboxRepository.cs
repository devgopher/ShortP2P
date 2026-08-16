using Microsoft.EntityFrameworkCore;
using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.Persistence.Psql.Entities;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.Persistence.Psql.Repositories;

public sealed class PostgresMessageInboxRepository(MessengerDbContext db) : IMessageInboxRepository
{
    public async Task AddAsync(MessageInboxEntry entry, CancellationToken cancellationToken = default)
    {
        var exists = await db.MessageInboxes
            .AnyAsync(x => x.MessageId == entry.MessageId && x.DeviceId == entry.DeviceId, cancellationToken)
            .ConfigureAwait(false);
        if (exists)
            return;

        db.MessageInboxes.Add(new MessageInboxRecord
        {
            MessageId = entry.MessageId,
            TgtNetworkId = entry.TgtNetworkId,
            DeviceId = entry.DeviceId
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<bool> ExistsAsync(
        string messageId,
        string deviceId,
        CancellationToken cancellationToken = default) =>
        db.MessageInboxes.AsNoTracking()
            .AnyAsync(x => x.MessageId == messageId && x.DeviceId == deviceId, cancellationToken);

    public async Task<IReadOnlyList<Message>> ListMessagesForDeviceAsync(
        string tgtNetworkId,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        var rows = await (
                from inbox in db.MessageInboxes.AsNoTracking()
                join msg in db.Messages.AsNoTracking() on inbox.MessageId equals msg.MessageId
                where inbox.TgtNetworkId == tgtNetworkId && inbox.DeviceId == deviceId
                orderby msg.CreatedUtc
                select msg)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(x => x.ToDomain()).ToArray();
    }

    public async Task RemoveAsync(
        string messageId,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        await db.MessageInboxes
            .Where(x => x.MessageId == messageId && x.DeviceId == deviceId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<int> CountForMessageAsync(string messageId, CancellationToken cancellationToken = default) =>
        db.MessageInboxes.CountAsync(x => x.MessageId == messageId, cancellationToken);

    public async Task RemoveAllForMessageAsync(string messageId, CancellationToken cancellationToken = default)
    {
        await db.MessageInboxes
            .Where(x => x.MessageId == messageId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
