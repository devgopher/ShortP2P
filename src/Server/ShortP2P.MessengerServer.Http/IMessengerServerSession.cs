namespace ShortP2P.MessengerServer.Http;

/// <summary>Holds the current JWT access token used for authenticated API calls.</summary>
public interface IMessengerServerSession
{
    string? AccessToken { get; }

    DateTime? ExpiresAtUtc { get; }

    bool HasValidToken { get; }

    void SetToken(string accessToken, DateTime expiresAtUtc);

    void Clear();
}
