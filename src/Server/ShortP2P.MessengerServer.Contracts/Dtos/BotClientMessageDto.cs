namespace ShortP2P.MessengerServer.Contracts.Dtos;

/// <summary>
/// Encrypted message between a client and a bot (either direction).
/// <see cref="NetworkId"/> is the client; server does not decrypt the payload.
/// </summary>
public sealed class BotClientMessageDto
{
    /// <summary>Client short network id (base64url, ~16 chars) — sender on income, recipient on send.</summary>
    public required string NetworkId { get; init; }

    /// <summary>Opaque ciphertext, base64.</summary>
    public required string EncryptedMessageBase64 { get; init; }
}
