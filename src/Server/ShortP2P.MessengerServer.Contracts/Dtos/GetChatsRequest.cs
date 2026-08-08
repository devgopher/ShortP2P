namespace ShortP2P.MessengerServer.Contracts.Dtos;

/// <summary>Request chats for a client by network id.</summary>
public sealed class GetChatsRequest
{
    /// <summary>Short network id of the client.</summary>
    public required string NetworkId { get; init; }
}
