namespace ShortP2P.MessengerServer.UseCases.Abstractions;

public sealed record AuthToken(string Token, DateTime ExpiresAtUtc);