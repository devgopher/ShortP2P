using ShortP2P.MessengerServer.UseCases.Abstractions;
using ShortP2P.TrustSystem;

namespace ShortP2P.MessengerServer.UseCases.Trust;

public sealed class AskRatingUseCase(
    TrustEngine engine,
    IClientAccountRepository accounts)
{
    public async Task<IReadOnlyList<RatedServer>> ExecuteAsync(
        string serverIp,
        int serverPort,
        CancellationToken cancellationToken = default)
    {
        var subscribers = await CountSubscribersAsync(accounts, cancellationToken).ConfigureAwait(false);
        try
        {
            return await engine.AskRatingAsync(serverIp, serverPort, subscribers, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TrustException ex)
        {
            throw UseCaseException.Validation(ex.Message);
        }
    }

    internal static async Task<int> CountSubscribersAsync(
        IClientAccountRepository accounts,
        CancellationToken cancellationToken)
    {
        var all = await accounts.ListAllAsync(cancellationToken).ConfigureAwait(false);
        return all.Select(a => a.NetworkId)
            .Distinct(StringComparer.Ordinal)
            .Count();
    }
}
