namespace ShortP2P.MessengerServer.Contracts.Dtos;

/// <summary>Acknowledgement for <see cref="BotSendMessageRequest"/>.</summary>
public sealed class BotSendMessageResponse
{
    /// <summary>Echo of the request message id.</summary>
    public required string MessageId { get; init; }

    /// <summary>
    /// 0 = success, 1 = internal error, 2 = wrong input.
    /// </summary>
    public required int ErrorCode { get; init; }

    /// <summary>Human-readable error detail; empty on success.</summary>
    public required string ErrorMessage { get; init; }
}
