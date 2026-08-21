using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.Api.Persistence.InMemory;

public sealed class InMemoryBlobRepository(InMemoryMessengerStore store) : IBlobRepository
{
    public Task<Blob?> FindByIdAsync(string blobId, CancellationToken cancellationToken = default)
    {
        store.Blobs.TryGetValue(blobId, out var blob);
        return Task.FromResult(blob);
    }

    public Task AddAsync(Blob blob, CancellationToken cancellationToken = default)
    {
        store.Blobs.TryAdd(blob.BlobId, blob);
        return Task.CompletedTask;
    }

    public Task RemoveByIdAsync(string blobId, CancellationToken cancellationToken = default)
    {
        store.Blobs.TryRemove(blobId, out _);
        return Task.CompletedTask;
    }

    public Task RemoveOlderThanAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default)
    {
        foreach (var id in store.Blobs.Values
                     .Where(b => b.CreatedUtc < cutoffUtc)
                     .Select(b => b.BlobId)
                     .ToArray())
            store.Blobs.TryRemove(id, out _);

        return Task.CompletedTask;
    }
}
