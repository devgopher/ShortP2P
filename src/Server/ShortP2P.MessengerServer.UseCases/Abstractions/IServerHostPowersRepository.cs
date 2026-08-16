using ShortP2P.MessengerServer.Domain;

namespace ShortP2P.MessengerServer.UseCases.Abstractions;

public interface IServerHostPowersRepository
{
    Task<ServerHostPowers> GetAsync(CancellationToken cancellationToken = default);

    Task UpsertAsync(ServerHostPowers powers, CancellationToken cancellationToken = default);
}
