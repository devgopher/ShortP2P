namespace ShortP2P.MessengerServer.UseCases.Auth;

public sealed record LoginClientCommand(string NetworkId, string Password);