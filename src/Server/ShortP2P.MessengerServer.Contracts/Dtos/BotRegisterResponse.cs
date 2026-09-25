namespace ShortP2P.MessengerServer.Contracts.Dtos;

/// <summary>Successful bot registration result.</summary>
public sealed class BotRegisterResponse
{
    /// <summary>Short network id (base64url, ~16 chars).</summary>
    public required string NetworkId { get; init; }

    /// <summary>Bot logical name.</summary>
    public required string BotName { get; init; }

    /// <summary>Human-readable display name.</summary>
    public required string BotReadableName { get; init; }

    /// <summary>Bot description.</summary>
    public required string BotDescription { get; init; }

    /// <summary>
    /// Bot key: base64, exactly <see cref="BotLimits.BotKeyLength"/> characters.
    /// <para><b>Secret:</b> known only to the server and the bot; must not be logged, relayed to clients, or shared otherwise.</para>
    /// </summary>
    public required string BotKey { get; init; }
}
