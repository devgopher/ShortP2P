namespace ShortP2P.MessengerServer.Contracts.Dtos;

/// <summary>Presence keep-alive.</summary>
public sealed class KeepAliveRequest
{
    public required string NetworkId { get; init; }
}
