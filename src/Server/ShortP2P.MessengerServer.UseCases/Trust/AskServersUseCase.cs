using ShortP2P.MessengerServer.UseCases.Abstractions;
using ShortP2P.TrustSystem;

namespace ShortP2P.MessengerServer.UseCases.Trust;

public sealed class AskServersUseCase(
    TrustEngine engine,
    IClientAccountRepository accounts)
{
    public async Task<IReadOnlyList<RatedServer>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var subscribers = await AskRatingUseCase.CountSubscribersAsync(accounts, cancellationToken)
            .ConfigureAwait(false);
        return await engine.AskServersAsync(subscribers, cancellationToken).ConfigureAwait(false);
    }
}
