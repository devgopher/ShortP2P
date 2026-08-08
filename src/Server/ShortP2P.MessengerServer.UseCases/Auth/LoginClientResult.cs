namespace ShortP2P.MessengerServer.UseCases.Auth;

public sealed record LoginClientResult(string Token, DateTime ExpiresAtUtc);