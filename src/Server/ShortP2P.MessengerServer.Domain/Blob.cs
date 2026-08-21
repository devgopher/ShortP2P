namespace ShortP2P.MessengerServer.Domain;

/// <summary>Store-and-forward encrypted attachment. Ciphertext is opaque to the server.</summary>
public sealed class Blob
{
    public required string BlobId { get; init; }

    public required string SrcNetworkId { get; init; }

    public required string TgtNetworkId { get; init; }

    /// <summary>Hybrid RSA-OAEP + AES-GCM envelope (same format as message payloads, without Base64).</summary>
    public required byte[] Ciphertext { get; init; }

    public required long SizeBytes { get; init; }

    public required DateTime CreatedUtc { get; init; }
}
