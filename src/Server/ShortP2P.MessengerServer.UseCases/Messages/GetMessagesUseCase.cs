using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.UseCases.Messages;

public sealed class GetMessagesUseCase(
    IMessageRepository messages,
    IMessageCache messageCache,
    MessengerCacheOptions options)
{
    public async Task<IReadOnlyList<Message>> ExecuteAsync(
        GetMessagesQuery query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query.CallerNetworkId))
            throw UseCaseException.Validation("CallerNetworkId is required.");

        StorageAccess.EnsureAnyStoreEnabled(options);

        var caller = query.CallerNetworkId.Trim();
        IReadOnlyList<Message> result = Array.Empty<Message>();

        if (options.CacheEnabled)
        {
            result = await StorageAccess
                .TryListAsync(() => messageCache.ListByTargetNetworkIdAsync(caller, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
        }

        if (result.Count == 0 && options.RepositoryEnabled)
        {
            result = await StorageAccess
                .TryListAsync(() => messages.ListByTargetNetworkIdAsync(caller, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
        }

        if (result.Count == 0)
            return result;

        var ids = result.Select(m => m.MessageId).ToArray();
        var removals = new List<Task>(2);
        if (options.CacheEnabled)
            removals.Add(StorageAccess.TryExecuteAsync(() => messageCache.RemoveByIdsAsync(ids, cancellationToken), cancellationToken));
        if (options.RepositoryEnabled)
            removals.Add(StorageAccess.TryExecuteAsync(() => messages.RemoveByIdsAsync(ids, cancellationToken), cancellationToken));
        await Task.WhenAll(removals).ConfigureAwait(false);

        return result;
    }
}
