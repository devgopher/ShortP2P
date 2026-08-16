namespace ShortP2P.MessengerServer.Contracts.Dtos;

/// <summary>Long-poll inbox snapshot for the authenticated device.</summary>
public sealed class EventsPollResponse
{
    public required IReadOnlyList<MessageDto> Messages { get; init; }

    public required IReadOnlyList<ChatRequestDto> ChatRequests { get; init; }
}
