namespace ShortP2P.MessengerServer.Domain;

/// <summary>Per-device inbox copy of a store-and-forward message.</summary>
public sealed class MessageInboxEntry
{
    public required string MessageId { get; init; }

    public required string TgtNetworkId { get; init; }

    public required string DeviceId { get; init; }
}
