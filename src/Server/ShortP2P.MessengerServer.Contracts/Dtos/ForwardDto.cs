namespace ShortP2P.MessengerServer.Contracts.Dtos;

/// <summary>Ephemeral forward delivered via events/poll (removed after delivery).</summary>
public sealed class ForwardDto
{
    public required string ForwardId { get; init; }

    public required string SrcNetworkId { get; init; }

    public required string TgtNetworkId { get; init; }

    public required ForwardKind Kind { get; init; }

    /// <summary>Full LAN wire frame 0x44 / 0x45, base64.</summary>
    public required string PayloadBase64 { get; init; }

    public required DateTime CreatedUtc { get; init; }
}
