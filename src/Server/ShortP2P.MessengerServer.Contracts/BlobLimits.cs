namespace ShortP2P.MessengerServer.Contracts;

/// <summary>Limits and headers for opaque encrypted attachment blobs.</summary>
public static class BlobLimits
{
    /// <summary>Max ciphertext size (covers 10 MiB document + hybrid envelope).</summary>
    public const int MaxCiphertextBytes = 12 * 1024 * 1024;

    public const string TargetNetworkIdHeader = "X-ShortP2P-Target-NetworkId";

    public static string BlobById(string blobId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blobId);
        return $"{ApiRoutes.Blobs}/{Uri.EscapeDataString(blobId.Trim())}";
    }
}
