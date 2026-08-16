namespace ShortP2P.MessengerServer.Domain;

/// <summary>Presence status of a registered client device.</summary>
public sealed class ClientStatuses
{
    public required string NetworkId { get; init; }

    /// <summary>Device id (64 lowercase hex SHA-256).</summary>
    public required string DeviceId { get; init; }

    public required ClientOnlineStatus Status { get; init; }

    /// <summary>Last status update time UTC (used as last-seen).</summary>
    public required DateTime CreatedAtUtc { get; init; }
}
