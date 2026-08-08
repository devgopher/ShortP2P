namespace ShortP2P.MessengerServer.Contracts.Dtos;

/// <summary>Successful login result.</summary>
public sealed class LoginResponse
{
    public required string Token { get; init; }

    /// <summary>Token expiry in UTC.</summary>
    public required DateTime ExpiresAtUtc { get; init; }
}
