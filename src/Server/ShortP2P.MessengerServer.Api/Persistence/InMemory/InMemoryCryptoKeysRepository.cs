using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.Api.Persistence.InMemory;

public sealed class InMemoryCryptoKeysRepository(InMemoryMessengerStore store) : ICryptoKeysRepository
{
    public Task UpsertAsync(CryptoKeys keys, CancellationToken cancellationToken = default)
    {
        store.CryptoKeys[(keys.SrcNetworkId, keys.TgtNetworkId)] = keys;
        return Task.CompletedTask;
    }
}
