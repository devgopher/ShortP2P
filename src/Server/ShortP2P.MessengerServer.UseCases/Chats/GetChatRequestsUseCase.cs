using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.UseCases.Chats;

/// <summary>
/// Delivers pending chat requests to the caller and removes them (DB / in-memory store).
/// </summary>
public sealed class GetChatRequestsUseCase(IChatRequestRepository chatRequests)
{
    public async Task<IReadOnlyList<ChatRequest>> ExecuteAsync(
        GetChatRequestsQuery query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query.CallerNetworkId))
            throw UseCaseException.Validation("CallerNetworkId is required.");

        // Take = list + delete: once returned to the client the request is accepted/consumed.
        return await chatRequests
            .TakeByTargetNetworkIdAsync(query.CallerNetworkId.Trim(), cancellationToken)
            .ConfigureAwait(false);
    }
}
