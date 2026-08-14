namespace ShortP2P.MessengerServer.Contracts.Dtos;

/// <summary>Registered client and current online/offline presence.</summary>
public sealed class ClientPresenceDto
{
    public required string NetworkId { get; init; }

    public required string Nick { get; init; }

    /// <summary><c>Online</c> or <c>Offline</c>.</summary>
    public required string Status { get; init; }

    /// <summary>Last keep-alive (or registration) time UTC.</summary>
    public required DateTime LastSeenAtUtc { get; init; }
}
