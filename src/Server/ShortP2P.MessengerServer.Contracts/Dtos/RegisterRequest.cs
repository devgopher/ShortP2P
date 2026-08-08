namespace ShortP2P.MessengerServer.Contracts.Dtos;

/// <summary>Client registration payload.</summary>
public sealed class RegisterRequest
{
    /// <summary>Display nickname.</summary>
    public required string Nick { get; init; }

    /// <summary>Short network id (base64url, ~16 chars).</summary>
    public required string NetworkId { get; init; }

    public required string Password { get; init; }
}
