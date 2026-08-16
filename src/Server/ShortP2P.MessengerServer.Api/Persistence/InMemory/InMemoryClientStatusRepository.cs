using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.Api.Persistence.InMemory;

public sealed class InMemoryClientStatusRepository(InMemoryMessengerStore store) : IClientStatusRepository
{
    public Task UpsertAsync(ClientStatuses status, CancellationToken cancellationToken = default)
    {
        store.Statuses[(status.NetworkId, status.DeviceId)] = status;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ClientStatuses>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ClientStatuses> list = store.Statuses.Values.ToArray();
        return Task.FromResult(list);
    }

    public Task<IReadOnlyList<string>> ListDeviceIdsAsync(
        string networkId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> ids = store.Statuses.Keys
            .Where(k => string.Equals(k.NetworkId, networkId, StringComparison.Ordinal))
            .Select(k => k.DeviceId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return Task.FromResult(ids);
    }
}
