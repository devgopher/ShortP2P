namespace ShortP2P.MessengerServer.Domain;

/// <summary>Per-device inbox copy of a chat request.</summary>
public sealed class ChatRequestInboxEntry
{
    public required string RequestId { get; init; }

    public required string TargetNetworkId { get; init; }

    public required string DeviceId { get; init; }
}
