namespace ShortP2P.MessengerServer.Contracts.Dtos;

/// <summary>Long-poll inbox snapshot for the authenticated device.</summary>
public sealed class EventsPollResponse
{
    public required IReadOnlyList<MessageDto> Messages { get; init; }

    public required IReadOnlyList<ChatRequestDto> ChatRequests { get; init; }

    /// <summary>Ephemeral peer-profile forwards; removed from server RAM after this poll.</summary>
    public IReadOnlyList<ForwardDto> Forwards { get; init; } = [];
}
