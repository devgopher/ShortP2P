namespace ShortP2P.MessengerServer.Contracts.Dtos;

/// <summary>Client authorization payload.</summary>
public sealed class LoginRequest
{
    /// <summary>Short network id (base64url, ~16 chars).</summary>
    public required string NetworkId { get; init; }

    public required string Password { get; init; }

    /// <summary>Device id: lowercase hex SHA-256 (64 chars) of install GUID.</summary>
    public required string DeviceId { get; init; }
}
