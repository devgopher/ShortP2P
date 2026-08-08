namespace ShortP2P.MessengerServer.Contracts.Dtos;

/// <summary>
/// Delivery receipt returned to a client.
/// GET receipts returns all existing receipts for the current client's networkId.
/// </summary>
public sealed class DeliveryReceiptDto
{
    public required string MessageId { get; init; }

    /// <summary>Receipt timestamp UTC.</summary>
    public required DateTime ReceivedAtUtc { get; init; }
}
