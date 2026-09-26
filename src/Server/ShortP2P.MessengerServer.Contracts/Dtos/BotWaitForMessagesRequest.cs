namespace ShortP2P.MessengerServer.Contracts.Dtos;

/// <summary>Long-poll request: wait for encrypted messages from bots to this client.</summary>
public sealed class BotWaitForMessagesRequest
{
    /// <summary>Client short network id (base64url, ~16 chars).</summary>
    public required string NetworkId { get; init; }

    /// <summary>Optional long-poll timeout in seconds (server clamps to its max).</summary>
    public int? TimeoutSeconds { get; init; }
}
