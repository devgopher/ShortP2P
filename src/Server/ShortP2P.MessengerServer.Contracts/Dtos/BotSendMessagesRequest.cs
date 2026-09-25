namespace ShortP2P.MessengerServer.Contracts.Dtos;

/// <summary>Bot → clients: deliver encrypted messages (store-and-forward).</summary>
public sealed class BotSendMessagesRequest
{
    /// <summary>Request correlation id (echoed in the response).</summary>
    public required string RequestId { get; init; }

    /// <summary>Bot short network id (base64url, ~16 chars).</summary>
    public required string BotNetworkId { get; init; }

    /// <summary>
    /// Bot key: base64, exactly <see cref="BotLimits.BotKeyLength"/> characters.
    /// <para><b>Secret:</b> known only to the server and the bot; must not be logged, relayed to clients, or shared otherwise.</para>
    /// </summary>
    public required string BotKey { get; init; }

    /// <summary>
    /// Outbound messages; <see cref="BotClientMessageDto.NetworkId"/> is the recipient client.
    /// </summary>
    public required IReadOnlyList<BotClientMessageDto> Messages { get; init; }
}
