using Microsoft.Extensions.Options;
using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.UseCases.Inbox;

/// <summary>Creates per-device inbox copies and lazy-fills for newly seen devices.</summary>
public sealed class DeviceFanoutService(
    IClientStatusRepository statuses,
    IMessageRepository messages,
    IMessageInboxRepository messageInbox,
    IMessageCache messageCache,
    IMessageInboxCache messageInboxCache,
    IChatRequestRepository chatRequests,
    MessengerCacheOptions cacheOptions,
    IOptions<MessengerInboxOptions> inboxOptions,
    IClock clock)
{
    public async Task<IReadOnlyList<string>> GetKnownDeviceIdsAsync(
        string networkId,
        CancellationToken cancellationToken = default)
    {
        return await statuses.ListDeviceIdsAsync(networkId.Trim(), cancellationToken).ConfigureAwait(false);
    }

    public async Task FanOutMessageAsync(Message message, CancellationToken cancellationToken = default)
    {
        var devices = await GetKnownDeviceIdsAsync(message.TgtNetworkId, cancellationToken).ConfigureAwait(false);
        foreach (var deviceId in devices)
            await AddMessageInboxIfMissingAsync(message, deviceId, cancellationToken).ConfigureAwait(false);
    }

    public async Task FanOutChatRequestAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        var devices = await GetKnownDeviceIdsAsync(request.TargetNetworkId, cancellationToken).ConfigureAwait(false);
        foreach (var deviceId in devices)
            await AddChatRequestInboxIfMissingAsync(request, deviceId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Lazy fan-out: ensure this device has inbox rows for undelivered messages/requests still within retention.
    /// </summary>
    public async Task EnsureInboxForDeviceAsync(
        string networkId,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        var net = networkId.Trim();
        var dev = deviceId.Trim();
        var cutoff = clock.UtcNow - inboxOptions.Value.MessageRetention;

        IReadOnlyList<Message> pendingMessages = [];
        if (cacheOptions.CacheEnabled)
        {
            pendingMessages = await StorageAccess
                .TryListAsync(() => messageCache.ListByTargetNetworkIdAsync(net, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
        }

        if (pendingMessages.Count == 0 && cacheOptions.RepositoryEnabled)
        {
            pendingMessages = await StorageAccess
                .TryListAsync(() => messages.ListByTargetNetworkIdAsync(net, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var message in pendingMessages)
        {
            if (message.CreatedUtc < cutoff)
                continue;
            await AddMessageInboxIfMissingAsync(message, dev, cancellationToken).ConfigureAwait(false);
        }

        var pendingRequests = await chatRequests
            .ListByTargetNetworkIdAsync(net, cancellationToken)
            .ConfigureAwait(false);

        foreach (var request in pendingRequests)
        {
            if (request.CreatedAtUtc < cutoff)
                continue;
            await AddChatRequestInboxIfMissingAsync(request, dev, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task AddMessageInboxIfMissingAsync(
        Message message,
        string deviceId,
        CancellationToken cancellationToken)
    {
        var entry = new MessageInboxEntry
        {
            MessageId = message.MessageId,
            TgtNetworkId = message.TgtNetworkId,
            DeviceId = deviceId
        };

        if (cacheOptions.CacheEnabled)
        {
            var existsInCache = false;
            try
            {
                existsInCache = await messageInboxCache
                    .ExistsAsync(message.MessageId, deviceId, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                // best-effort
            }

            if (!existsInCache)
            {
                await StorageAccess.TryWriteToCacheAsync(
                    cacheEnabled: true,
                    () => messageInboxCache.IsWriteAvailable,
                    () => messageInboxCache.AddAsync(entry, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            }
        }

        if (cacheOptions.RepositoryEnabled)
        {
            try
            {
                if (!await messageInbox.ExistsAsync(message.MessageId, deviceId, cancellationToken).ConfigureAwait(false))
                    await messageInbox.AddAsync(entry, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // best-effort when store flaps
            }
        }
    }

    private async Task AddChatRequestInboxIfMissingAsync(
        ChatRequest request,
        string deviceId,
        CancellationToken cancellationToken)
    {
        if (await chatRequests.InboxExistsAsync(request.RequestId, deviceId, cancellationToken).ConfigureAwait(false))
            return;

        await chatRequests.AddInboxAsync(
            new ChatRequestInboxEntry
            {
                RequestId = request.RequestId,
                TargetNetworkId = request.TargetNetworkId,
                DeviceId = deviceId
            },
            cancellationToken).ConfigureAwait(false);
    }
}
