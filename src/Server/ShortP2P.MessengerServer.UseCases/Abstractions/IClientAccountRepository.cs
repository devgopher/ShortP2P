using ShortP2P.MessengerServer.Domain;

namespace ShortP2P.MessengerServer.UseCases.Abstractions;

public interface IClientAccountRepository
{
    Task<ClientAccount?> FindByNetworkIdAsync(string networkId, CancellationToken cancellationToken = default);

    Task<ClientAccount?> FindByNickAsync(string nick, CancellationToken cancellationToken = default);

    Task AddAsync(ClientAccount account, CancellationToken cancellationToken = default);
}
