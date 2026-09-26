namespace ShortP2P.MessengerServer.Contracts.Dtos;

/// <summary>Long-poll result: bot messages waiting for the client (may be empty on timeout).</summary>
public sealed class BotWaitForMessagesResponse
{
    /// <summary>Messages from bots; empty if the long-poll timed out with nothing queued.</summary>
    public required IReadOnlyList<BotClientInboundMessageDto> Messages { get; init; }
}
