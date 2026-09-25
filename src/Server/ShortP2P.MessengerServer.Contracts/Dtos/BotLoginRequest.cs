namespace ShortP2P.MessengerServer.Contracts.Dtos;

/// <summary>Bot authorization payload.</summary>
public sealed class BotLoginRequest
{
    /// <summary>Short network id (base64url, ~16 chars).</summary>
    public required string NetworkId { get; init; }

    /// <summary>Bot logical name (max <see cref="BotLimits.MaxBotNameLength"/> chars).</summary>
    public required string BotName { get; init; }
}
