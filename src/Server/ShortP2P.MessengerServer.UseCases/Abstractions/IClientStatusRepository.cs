using ShortP2P.MessengerServer.Domain;

namespace ShortP2P.MessengerServer.UseCases.Abstractions;

public interface IClientStatusRepository
{
    Task UpsertAsync(ClientStatuses status, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClientStatuses>> ListAllAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ListDeviceIdsAsync(
        string networkId,
        CancellationToken cancellationToken = default);
}
