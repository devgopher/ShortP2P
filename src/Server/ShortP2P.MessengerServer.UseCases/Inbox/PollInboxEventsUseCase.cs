using Microsoft.Extensions.Options;
using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.UseCases.Inbox;

public sealed record PollInboxEventsQuery(
    string CallerNetworkId,
    string DeviceId,
    int? TimeoutSeconds);

public sealed record PollInboxEventsResult(
    IReadOnlyList<Message> Messages,
    IReadOnlyList<ChatRequest> ChatRequests);

public sealed class PollInboxEventsUseCase(
    IMessageRepository messages,
    IMessageCache messageCache,
    IMessageInboxRepository messageInbox,
    IMessageInboxCache messageInboxCache,
    IChatRequestRepository chatRequests,
    IInboxWaitService inboxWait,
    MessengerCacheOptions cacheOptions,
    IOptions<MessengerInboxOptions> inboxOptions,
    IClock clock)
{
    public async Task<PollInboxEventsResult> ExecuteAsync(
        PollInboxEventsQuery query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query.CallerNetworkId) || string.IsNullOrWhiteSpace(query.DeviceId))
            throw UseCaseException.Validation("CallerNetworkId and deviceId are required.");

        if (!DeviceIdRules.IsValid(query.DeviceId.Trim()))
            throw UseCaseException.Validation("DeviceId must be 64 lowercase hex characters (SHA-256).");

        var caller = query.CallerNetworkId.Trim();
        var deviceId = query.DeviceId.Trim();
        var opts = inboxOptions.Value;
        var timeoutSeconds = query.TimeoutSeconds ?? opts.MaxPollTimeoutSeconds;
        if (timeoutSeconds < 1 || timeoutSeconds > opts.MaxPollTimeoutSeconds)
            throw UseCaseException.Validation($"timeoutSeconds must be between 1 and {opts.MaxPollTimeoutSeconds}.");

        var cutoff = clock.UtcNow - opts.MessageRetention;

        var snapshot = await ReadInboxAsync(caller, deviceId, cutoff, cancellationToken).ConfigureAwait(false);
        if (snapshot.Messages.Count > 0 || snapshot.ChatRequests.Count > 0)
            return snapshot;

        await inboxWait
            .WaitAsync(caller, deviceId, TimeSpan.FromSeconds(timeoutSeconds), cancellationToken)
            .ConfigureAwait(false);

        return await ReadInboxAsync(caller, deviceId, cutoff, cancellationToken).ConfigureAwait(false);
    }

    private async Task<PollInboxEventsResult> ReadInboxAsync(
        string caller,
        string deviceId,
        DateTime cutoffUtc,
        CancellationToken cancellationToken)
    {
        var messageList = await ListMessagesForDeviceAsync(caller, deviceId, cancellationToken).ConfigureAwait(false);
        messageList = messageList.Where(m => m.CreatedUtc >= cutoffUtc).ToArray();

        var requests = await chatRequests
            .TakeForDeviceAsync(caller, deviceId, cancellationToken)
            .ConfigureAwait(false);
        requests = [.. requests.Where(r => r.CreatedAtUtc >= cutoffUtc)];

        return new PollInboxEventsResult(messageList, requests);
    }

    private async Task<IReadOnlyList<Message>> ListMessagesForDeviceAsync(
        string caller,
        string deviceId,
        CancellationToken cancellationToken)
    {
        if (cacheOptions.RepositoryEnabled)
        {
            var fromRepo = await StorageAccess
                .TryListAsync(
                    () => messageInbox.ListMessagesForDeviceAsync(caller, deviceId, cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);
            if (fromRepo.Count > 0)
                return fromRepo;
        }

        if (cacheOptions.CacheEnabled)
        {
            var ids = await StorageAccess
                .TryListAsync(
                    () => messageInboxCache.ListMessageIdsForDeviceAsync(caller, deviceId, cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);
            if (ids.Count == 0)
                return [];

            var list = new List<Message>(ids.Count);
            foreach (var id in ids)
            {
                var msg = await StorageAccess
                    .TryGetAsync(() => messageCache.FindByIdAsync(id, cancellationToken), cancellationToken)
                    .ConfigureAwait(false);
                if (msg is null && cacheOptions.RepositoryEnabled)
                {
                    msg = await StorageAccess
                        .TryGetAsync(() => messages.FindByIdAsync(id, cancellationToken), cancellationToken)
                        .ConfigureAwait(false);
                }

                if (msg is not null)
                    list.Add(msg);
            }

            return list;
        }

        return [];
    }
}
