using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.UseCases.Presence;

public sealed class KeepAliveUseCase(IClientStatusRepository statuses, IClock clock)
{
    public async Task ExecuteAsync(KeepAliveCommand command, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.NetworkId))
            throw UseCaseException.Validation("NetworkId is required.");

        await statuses.UpsertAsync(
            new ClientStatuses
            {
                NetworkId = command.NetworkId.Trim(),
                Status = ClientOnlineStatus.Online,
                CreatedAtUtc = clock.UtcNow
            },
            cancellationToken).ConfigureAwait(false);
    }
}
