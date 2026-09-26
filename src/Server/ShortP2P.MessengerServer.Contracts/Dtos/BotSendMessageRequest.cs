namespace ShortP2P.MessengerServer.Contracts.Dtos;

/// <summary>Client → bot: deliver one encrypted message (store-and-forward).</summary>
public sealed class BotSendMessageRequest
{
    /// <summary>Message correlation id.</summary>
    public required string MessageId { get; init; }

    /// <summary>Destination bot short network id (base64url, ~16 chars).</summary>
    public required string NetworkId { get; init; }

    /// <summary>Message timestamp (UTC).</summary>
    public required DateTime DateTime { get; init; }

    /// <summary>Opaque ciphertext, base64.</summary>
    public required string Message { get; init; }
}
