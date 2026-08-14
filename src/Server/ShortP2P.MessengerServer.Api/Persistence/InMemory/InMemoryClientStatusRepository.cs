using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.Api.Persistence.InMemory;

public sealed class InMemoryClientStatusRepository(InMemoryMessengerStore store) : IClientStatusRepository
{
    public Task UpsertAsync(ClientStatuses status, CancellationToken cancellationToken = default)
    {
        store.Statuses[status.NetworkId] = status;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ClientStatuses>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ClientStatuses> list = store.Statuses.Values.ToArray();
        return Task.FromResult(list);
    }
}
