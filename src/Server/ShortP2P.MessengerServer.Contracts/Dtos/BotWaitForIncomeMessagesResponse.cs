namespace ShortP2P.MessengerServer.Contracts.Dtos;

/// <summary>Long-poll result: client messages waiting for the bot (may be empty on timeout).</summary>
public sealed class BotWaitForIncomeMessagesResponse
{
    /// <summary>Echo of the request correlation id.</summary>
    public required string RequestId { get; init; }

    /// <summary>Messages from clients; empty if the long-poll timed out with nothing queued.</summary>
    public required IReadOnlyList<BotClientMessageDto> Messages { get; init; }
}
