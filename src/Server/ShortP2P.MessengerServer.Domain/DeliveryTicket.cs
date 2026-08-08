namespace ShortP2P.MessengerServer.Domain;

/// <summary>Delivery receipt (ticket) for a message.</summary>
public sealed class DeliveryTicket
{
    public required string MessageId { get; init; }

    /// <summary>Receipt timestamp UTC.</summary>
    public required DateTime ReceivedAtUtc { get; init; }
}
