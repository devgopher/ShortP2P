using ShortP2P.MessengerServer.Domain;

namespace ShortP2P.MessengerServer.UseCases.Messages;

public sealed record SendMessageCommand(Message Message);