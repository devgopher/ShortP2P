namespace ShortP2P.MessengerServer.Contracts.Dtos;

/// <summary>Inbound chat registration request for the current client.</summary>
public sealed class ChatRequestDto
{
    /// <summary>Requester short network id.</summary>
    public required string NetworkId { get; init; }

    /// <summary>Requester public key.</summary>
    public required string PublicKey { get; init; }
}
