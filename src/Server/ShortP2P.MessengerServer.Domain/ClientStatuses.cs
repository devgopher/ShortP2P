namespace ShortP2P.MessengerServer.Domain;

/// <summary>Presence status of a registered client.</summary>
public sealed class ClientStatuses
{
    public required string NetworkId { get; init; }

    public required ClientOnlineStatus Status { get; init; }

    /// <summary>Creation time UTC.</summary>
    public required DateTime CreatedAtUtc { get; init; }
}
