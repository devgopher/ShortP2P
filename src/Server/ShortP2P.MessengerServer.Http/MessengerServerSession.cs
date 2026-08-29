namespace ShortP2P.MessengerServer.Http;

/// <summary>In-memory JWT session for the messenger server HTTP client.</summary>
public sealed class MessengerServerSession : IMessengerServerSession
{
    private readonly object _gate = new();
    private string? _accessToken;
    private DateTime? _expiresAtUtc;

    public string? AccessToken
    {
        get
        {
            lock (_gate)
                return _accessToken;
        }
    }

    public DateTime? ExpiresAtUtc
    {
        get
        {
            lock (_gate)
                return _expiresAtUtc;
        }
    }

    public bool HasValidToken
    {
        get
        {
            lock (_gate)
            {
                return !string.IsNullOrWhiteSpace(_accessToken)
                       && _expiresAtUtc is { } exp
                       && exp > DateTime.UtcNow.AddSeconds(30);
            }
        }
    }

    public void SetToken(string accessToken, DateTime expiresAtUtc)
    {
        Require.NotNullOrWhiteSpace(accessToken);

        lock (_gate)
        {
            _accessToken = accessToken.Trim();
            _expiresAtUtc = expiresAtUtc.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(expiresAtUtc, DateTimeKind.Utc)
                : expiresAtUtc.ToUniversalTime();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _accessToken = null;
            _expiresAtUtc = null;
        }
    }
}
