using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.UseCases.Blobs;

public sealed class GetBlobUseCase(IBlobRepository blobs)
{
    public async Task<Blob> ExecuteAsync(GetBlobQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var blobId = query.BlobId?.Trim() ?? "";
        var caller = query.CallerNetworkId?.Trim() ?? "";
        if (blobId.Length == 0)
            throw UseCaseException.Validation("blobId is required.");
        if (caller.Length == 0)
            throw UseCaseException.Unauthorized("Missing network id.");

        var blob = await blobs.FindByIdAsync(blobId, cancellationToken).ConfigureAwait(false);
        if (blob == null)
            throw UseCaseException.NotFound("Blob not found.");

        if (!string.Equals(blob.SrcNetworkId, caller, StringComparison.Ordinal) &&
            !string.Equals(blob.TgtNetworkId, caller, StringComparison.Ordinal))
            throw UseCaseException.Unauthorized("Not allowed to download this blob.");

        return blob;
    }
}
