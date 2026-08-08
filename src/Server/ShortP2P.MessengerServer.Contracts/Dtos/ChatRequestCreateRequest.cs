namespace ShortP2P.MessengerServer.Contracts.Dtos;

/// <summary>Outbound request to register a new chat with a subscriber.</summary>
public sealed class ChatRequestCreateRequest
{
    /// <summary>Requester's public key (opaque string, e.g. RSA public JSON).</summary>
    public required string PublicKey { get; init; }

    /// <summary>Target subscriber short network id.</summary>
    public required string TargetNetworkId { get; init; }
}
