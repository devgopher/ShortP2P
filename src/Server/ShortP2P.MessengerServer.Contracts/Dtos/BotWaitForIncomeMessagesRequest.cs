namespace ShortP2P.MessengerServer.Contracts.Dtos;

/// <summary>Long-poll request: wait for client messages addressed to the bot.</summary>
public sealed class BotWaitForIncomeMessagesRequest
{
    /// <summary>
    ///     RequestId
    /// </summary>
    public required string RequestId { get; set; }

    /// <summary>Bot short network id (base64url, ~16 chars).</summary>
    public required string BotNetworkId { get; init; }

    /// <summary>
    ///     Bot key: base64, exactly <see cref="BotLimits.BotKeyLength" /> characters.
    ///     <para>
    ///         <b>Secret:</b> known only to the server and the bot; must not be logged, relayed to clients, or shared
    ///         otherwise.
    ///     </para>
    /// </summary>
    public required string BotKey { get; init; }

    /// <summary>Optional long-poll timeout in seconds (server clamps to its max).</summary>
    public int? TimeoutSeconds { get; init; }
}