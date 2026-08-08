using ShortP2P.MessengerServer.Domain;

namespace ShortP2P.MessengerServer.UseCases.Abstractions;

public interface ICryptoKeysRepository
{
    Task UpsertAsync(CryptoKeys keys, CancellationToken cancellationToken = default);
}
