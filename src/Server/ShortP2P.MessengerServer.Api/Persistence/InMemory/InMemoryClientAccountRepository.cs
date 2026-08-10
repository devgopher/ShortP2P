using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.Api.Persistence.InMemory;

public sealed class InMemoryClientAccountRepository(InMemoryMessengerStore store) : IClientAccountRepository
{
    public Task<ClientAccount?> FindByNetworkIdAsync(string networkId, CancellationToken cancellationToken = default)
    {
        store.AccountsByNetworkId.TryGetValue(networkId, out var account);
        return Task.FromResult(account);
    }

    public Task<ClientAccount?> FindByNickAsync(string nick, CancellationToken cancellationToken = default)
    {
        if (!store.NetworkIdByNick.TryGetValue(nick, out var networkId))
            return Task.FromResult<ClientAccount?>(null);

        store.AccountsByNetworkId.TryGetValue(networkId, out var account);
        return Task.FromResult(account);
    }

    public Task AddAsync(ClientAccount account, CancellationToken cancellationToken = default)
    {
        if (!store.AccountsByNetworkId.TryAdd(account.NetworkId, account))
            throw new InvalidOperationException($"Account '{account.NetworkId}' already exists.");

        if (!store.NetworkIdByNick.TryAdd(account.Nick, account.NetworkId))
        {
            store.AccountsByNetworkId.TryRemove(account.NetworkId, out _);
            throw new InvalidOperationException($"Nick '{account.Nick}' already exists.");
        }

        return Task.CompletedTask;
    }
}
