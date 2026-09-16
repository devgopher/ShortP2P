namespace ShortP2P.MessengerServer.Contracts.Dtos;

/// <summary>POST /api/v1/forward body — ephemeral dumb-pipe (no durable profile store).</summary>
public sealed class ForwardRequest
{
    /// <summary>Client-generated id (UUID or similar); required for idempotency.</summary>
    public required string ForwardId { get; init; }

    public required string SrcNetworkId { get; init; }

    public required string TgtNetworkId { get; init; }

    public required ForwardKind Kind { get; init; }

    /// <summary>Full LAN wire frame 0x44 / 0x45, base64.</summary>
    public required string PayloadBase64 { get; init; }
}
