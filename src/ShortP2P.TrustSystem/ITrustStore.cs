namespace ShortP2P.TrustSystem;

public interface ITrustStore
{
    Task<ServerTrustState?> GetAsync(ServerEndpoint endpoint, CancellationToken cancellationToken = default);

    Task UpsertAsync(ServerTrustState state, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ServerTrustState>> ListAsync(CancellationToken cancellationToken = default);
}
