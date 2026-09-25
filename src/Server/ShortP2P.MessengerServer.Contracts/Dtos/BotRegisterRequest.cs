namespace ShortP2P.MessengerServer.Contracts.Dtos;

/// <summary>New bot registration payload.</summary>
public sealed class BotRegisterRequest
{
    /// <summary>Short network id (base64url, ~16 chars).</summary>
    public required string NetworkId { get; init; }

    /// <summary>Bot logical name (max <see cref="BotLimits.MaxBotNameLength"/> chars).</summary>
    public required string BotName { get; init; }

    /// <summary>Human-readable display name (max <see cref="BotLimits.MaxBotReadableNameLength"/> chars).</summary>
    public required string BotReadableName { get; init; }

    /// <summary>Bot description (max <see cref="BotLimits.MaxBotDescriptionLength"/> chars).</summary>
    public required string BotDescription { get; init; }
}
