namespace ShortP2P.MessengerServer.Contracts.Dtos;

/// <summary>Bot authorization payload.</summary>
public sealed class BotLoginRequest
{
    /// <summary>Short network id (base64url, ~16 chars).</summary>
    public required string NetworkId { get; init; }

    /// <summary>Bot logical name (max <see cref="BotLimits.MaxBotNameLength"/> chars).</summary>
    public required string BotName { get; init; }

    /// <summary>
    /// Bot key: base64, exactly <see cref="BotLimits.BotKeyLength"/> characters.
    /// Issued by the server on registration; known only to the server and the bot.
    /// <para><b>Secret:</b> must not be logged, relayed to clients, or shared otherwise.</para>
    /// </summary>
    public required string BotKey { get; init; }
}
