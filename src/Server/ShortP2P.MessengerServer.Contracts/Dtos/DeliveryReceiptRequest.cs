namespace ShortP2P.MessengerServer.Contracts.Dtos;

/// <summary>Client submits a delivery receipt for a received message.</summary>
public sealed class DeliveryReceiptRequest
{
    public required string MessageId { get; init; }

    /// <summary>Receipt timestamp UTC.</summary>
    public required DateTime ReceivedAtUtc { get; init; }
}
