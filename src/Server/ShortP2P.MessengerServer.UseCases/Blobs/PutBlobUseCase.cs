using ShortP2P.MessengerServer.Domain;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.UseCases.Blobs;

public sealed class PutBlobUseCase(IBlobRepository blobs, IClock clock)
{
    public const int MaxCiphertextBytes = 12 * 1024 * 1024;

    public async Task ExecuteAsync(PutBlobCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var blobId = command.BlobId?.Trim() ?? "";
        var src = command.SrcNetworkId?.Trim() ?? "";
        var tgt = command.TgtNetworkId?.Trim() ?? "";
        var ciphertext = command.Ciphertext ?? [];

        if (blobId.Length is 0 or > 128)
            throw UseCaseException.Validation("blobId is required (max 128 characters).");
        if (src.Length == 0)
            throw UseCaseException.Validation("srcNetworkId is required.");
        if (tgt.Length == 0)
            throw UseCaseException.Validation("tgtNetworkId is required.");
        if (ciphertext.Length == 0)
            throw UseCaseException.Validation("Ciphertext is required.");
        if (ciphertext.Length > MaxCiphertextBytes)
        {
            throw UseCaseException.Validation(
                $"Ciphertext exceeds the maximum of {MaxCiphertextBytes} bytes.");
        }

        var existing = await blobs.FindByIdAsync(blobId, cancellationToken).ConfigureAwait(false);
        if (existing != null)
        {
            if (string.Equals(existing.SrcNetworkId, src, StringComparison.Ordinal) &&
                string.Equals(existing.TgtNetworkId, tgt, StringComparison.Ordinal))
                return;

            throw UseCaseException.Conflict("A blob with this id already exists.");
        }

        var now = clock.UtcNow;
        await blobs.AddAsync(
            new Blob
            {
                BlobId = blobId,
                SrcNetworkId = src,
                TgtNetworkId = tgt,
                Ciphertext = ciphertext,
                SizeBytes = ciphertext.Length,
                CreatedUtc = now
            },
            cancellationToken).ConfigureAwait(false);
    }
}
