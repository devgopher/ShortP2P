using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.UseCases.Messages;

public sealed class GetMessagesUseCase(IMessageRepository messages, IMessageCache messageCache)
{
    public async Task<IReadOnlyList<Message>> ExecuteAsync(
        GetMessagesQuery query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query.CallerNetworkId))
            throw UseCaseException.Validation("CallerNetworkId is required.");

        var caller = query.CallerNetworkId.Trim();

        var cached = await messageCache.ListByTargetNetworkIdAsync(caller, cancellationToken)
            .ConfigureAwait(false);
        var result = cached.Count > 0
            ? cached
            : await messages.ListByTargetNetworkIdAsync(caller, cancellationToken).ConfigureAwait(false);

        if (result.Count == 0)
            return result;

        var ids = result.Select(m => m.MessageId).ToArray();
        await Task.WhenAll(
            messageCache.RemoveByIdsAsync(ids, cancellationToken),
            messages.RemoveByIdsAsync(ids, cancellationToken)).ConfigureAwait(false);

        return result;
    }
}
