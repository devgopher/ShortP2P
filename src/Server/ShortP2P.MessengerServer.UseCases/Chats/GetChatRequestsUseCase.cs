using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.UseCases.Chats;

public sealed class GetChatRequestsUseCase(IChatRequestRepository chatRequests)
{
    public async Task<IReadOnlyList<ChatRequest>> ExecuteAsync(
        GetChatRequestsQuery query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query.CallerNetworkId))
            throw UseCaseException.Validation("CallerNetworkId is required.");

        return await chatRequests
            .ListByTargetNetworkIdAsync(query.CallerNetworkId.Trim(), cancellationToken)
            .ConfigureAwait(false);
    }
}
