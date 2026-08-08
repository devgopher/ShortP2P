using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.UseCases.Chats;

public sealed class GetChatsUseCase(IChatRepository chats)
{
    public async Task<IReadOnlyList<Chat>> ExecuteAsync(
        GetChatsQuery query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query.NetworkId))
            throw UseCaseException.Validation("NetworkId is required.");

        return await chats.ListByNetworkIdAsync(query.NetworkId.Trim(), cancellationToken).ConfigureAwait(false);
    }
}
