namespace ShortP2P.MessengerServer.Domain;

/// <summary>Pending request to start a chat with a target subscriber.</summary>
public sealed class ChatRequest
{
    /// <summary>Stable id shared across device fan-out copies.</summary>
    public required string RequestId { get; init; }

    public required string RequesterNetworkId { get; init; }

    public required string TargetNetworkId { get; init; }

    /// <summary>Requester public key.</summary>
    public required string PublicKey { get; init; }

    /// <summary>Creation time UTC.</summary>
    public required DateTime CreatedAtUtc { get; init; }
}
