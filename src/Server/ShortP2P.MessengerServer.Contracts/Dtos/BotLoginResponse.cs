namespace ShortP2P.MessengerServer.Contracts.Dtos;

/// <summary>Successful bot authorization result.</summary>
public sealed class BotLoginResponse
{
    public required string Token { get; init; }

    /// <summary>Token expiry in UTC.</summary>
    public required DateTime ExpiresAtUtc { get; init; }
}
