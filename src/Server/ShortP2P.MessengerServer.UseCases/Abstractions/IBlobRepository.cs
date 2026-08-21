using ShortP2P.MessengerServer.Domain;

namespace ShortP2P.MessengerServer.UseCases.Abstractions;

public interface IBlobRepository
{
    Task<Blob?> FindByIdAsync(string blobId, CancellationToken cancellationToken = default);

    Task AddAsync(Blob blob, CancellationToken cancellationToken = default);

    Task RemoveByIdAsync(string blobId, CancellationToken cancellationToken = default);

    Task RemoveOlderThanAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default);
}
