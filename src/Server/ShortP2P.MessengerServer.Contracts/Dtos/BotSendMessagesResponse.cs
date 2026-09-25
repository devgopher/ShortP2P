namespace ShortP2P.MessengerServer.Contracts.Dtos;

/// <summary>Acknowledgement for <see cref="BotSendMessagesRequest"/>.</summary>
public sealed class BotSendMessagesResponse
{
    /// <summary>Echo of the request correlation id.</summary>
    public required string RequestId { get; init; }
}
