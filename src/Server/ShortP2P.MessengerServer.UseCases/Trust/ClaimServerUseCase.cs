using ShortP2P.MessengerServer.UseCases.Abstractions;
using ShortP2P.TrustSystem;

namespace ShortP2P.MessengerServer.UseCases.Trust;

public sealed class ClaimServerUseCase(
    TrustEngine engine,
    IClientAccountRepository accounts)
{
    public async Task ExecuteAsync(
        string callerNetworkId,
        string serverIp,
        int serverPort,
        ServerClaimReason reason,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(callerNetworkId))
            throw UseCaseException.Unauthorized("Missing network id claim.");

        var subscribers = await AskRatingUseCase.CountSubscribersAsync(accounts, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await engine.ClaimServerAsync(
                    serverIp,
                    serverPort,
                    reason,
                    callerNetworkId.Trim(),
                    subscribers,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TrustException ex)
        {
            throw UseCaseException.Validation(ex.Message);
        }
    }
}
