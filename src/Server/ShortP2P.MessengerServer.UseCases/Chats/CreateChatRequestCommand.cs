namespace ShortP2P.MessengerServer.UseCases.Chats;

public sealed record CreateChatRequestCommand(
    string CallerNetworkId,
    string PublicKey,
    string TargetNetworkId);