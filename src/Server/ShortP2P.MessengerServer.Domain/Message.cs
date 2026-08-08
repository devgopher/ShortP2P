namespace ShortP2P.MessengerServer.Domain;

/// <summary>Store-and-forward encrypted message. Ciphertext is opaque to the server.</summary>
public sealed class Message
{
    public required string MessageId { get; init; }

    public required string SrcNetworkId { get; init; }

    public required string TgtNetworkId { get; init; }

    /// <summary>Creation time UTC.</summary>
    public required DateTime CreatedUtc { get; init; }

    /// <summary>Last update time UTC.</summary>
    public required DateTime UpdatedUtc { get; init; }

    /// <summary>Opaque ciphertext, base64.</summary>
    public required string EncryptedDataBase64 { get; init; }
}
