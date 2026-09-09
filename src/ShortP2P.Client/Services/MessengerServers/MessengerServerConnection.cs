using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ShortP2P.Client.Data;
using ShortP2P.MessengerServer.Contracts;
using ShortP2P.MessengerServer.Http;

namespace ShortP2P.Client.Services.MessengerServers;

/// <summary>Live HTTPS session to one messenger server (pinned TLS fingerprint).</summary>
public sealed class MessengerServerConnection : IAsyncDisposable
{
    private readonly HttpClient _commandHttp;
    private readonly HttpClient _longPollHttp;
    private readonly MessengerServerSession _session;
    private readonly ConnectionPinHolder _pinHolder;
    private bool _disposed;

    private MessengerServerConnection(
        MessengerServerEntity entity,
        HttpClient commandHttp,
        HttpClient longPollHttp,
        MessengerServerSession session,
        MessengerServerApiClient commandApi,
        MessengerServerApiClient longPollApi,
        ConnectionPinHolder pinHolder)
    {
        Entity = entity;
        _commandHttp = commandHttp;
        _longPollHttp = longPollHttp;
        _session = session;
        Api = commandApi;
        LongPollApi = longPollApi;
        _pinHolder = pinHolder;
    }

    public MessengerServerEntity Entity { get; private set; }

    /// <summary>Short requests: send, receipts, GetClients, login, ping — must not share sockets with long-poll.</summary>
    public IMessengerServerApi Api { get; }

    /// <summary>Dedicated client for inbox long-poll so a 25s waiter cannot stall outbound SendMessage.</summary>
    public IMessengerServerApi LongPollApi { get; }

    public bool HasValidToken => _session.HasValidToken;

    public static MessengerServerConnection Create(MessengerServerEntity entity, TimeSpan timeout)
    {
        Require.NotNull(entity);

        var session = new MessengerServerSession();
        var pinHolder = new ConnectionPinHolder
        {
            PinnedFingerprintSha256 = NormalizeFingerprint(entity.FingerprintSha256),
            RequirePin = entity.Trusted && !string.IsNullOrWhiteSpace(entity.FingerprintSha256)
        };

        var baseUri = new Uri(SqliteMessengerServerRepository.NormalizeBaseUrl(entity.BaseUrl) + "/");

        // Separate handlers = separate connection pools. One long-poll HTTP/1.1 socket must not
        // block SendMessage / GetClients on platforms with a low MaxConnectionsPerServer.
        var commandHttp = CreateHttpClient(session, pinHolder, baseUri, timeout, maxConnectionsPerServer: 8);
        var longPollHttp = CreateHttpClient(session, pinHolder, baseUri, timeout, maxConnectionsPerServer: 2);

        var commandApi = new MessengerServerApiClient(commandHttp, session);
        var longPollApi = new MessengerServerApiClient(longPollHttp, session);
        return new MessengerServerConnection(
            entity, commandHttp, longPollHttp, session, commandApi, longPollApi, pinHolder);
    }

    private static HttpClient CreateHttpClient(
        MessengerServerSession session,
        ConnectionPinHolder pinHolder,
        Uri baseUri,
        TimeSpan timeout,
        int maxConnectionsPerServer)
    {
#if NETFRAMEWORK
        System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
        // Per-host default is often 2; raise it so command + long-poll can overlap.
        try
        {
            var sp = System.Net.ServicePointManager.FindServicePoint(baseUri);
            if (sp.ConnectionLimit < maxConnectionsPerServer)
                sp.ConnectionLimit = maxConnectionsPerServer;
        }
        catch
        {
            // ignore ServicePoint tuning failures
        }

        HttpMessageHandler sockets = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, cert, _, _) =>
                ValidateCertificate(cert, pinHolder.PinnedFingerprintSha256, pinHolder.RequirePin)
        };
#else
        var sockets = new SocketsHttpHandler
        {
            MaxConnectionsPerServer = maxConnectionsPerServer,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, cert, _, _) =>
                    ValidateCertificate(cert, pinHolder.PinnedFingerprintSha256, pinHolder.RequirePin)
            }
        };
#endif

        var http = new HttpClient(new MessengerServerBearerHandler(session) { InnerHandler = sockets })
        {
            BaseAddress = baseUri,
            Timeout = timeout
        };
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/octet-stream");
        return http;
    }

    /// <summary>Bootstrap client without pin (first certificate fetch when adding a server).</summary>
    public static MessengerServerConnection CreateBootstrap(string baseUrl, TimeSpan timeout)
    {
        var entity = new MessengerServerEntity
        {
            BaseUrl = SqliteMessengerServerRepository.NormalizeBaseUrl(baseUrl),
            Trusted = false,
            Active = true
        };
        return Create(entity, timeout);
    }

    public void UpdateEntity(MessengerServerEntity entity)
    {
        Require.NotNull(entity);
        Entity = entity;
        _pinHolder.PinnedFingerprintSha256 = NormalizeFingerprint(entity.FingerprintSha256);
        _pinHolder.RequirePin = entity.Trusted && !string.IsNullOrWhiteSpace(entity.FingerprintSha256);
    }

    public void ClearSession() => _session.Clear();

    public static string NormalizeFingerprint(string? fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
            return "";
        var sb = new System.Text.StringBuilder(fingerprint.Length);
        foreach (var c in fingerprint.Trim())
        {
            if (c is ':' or ' ' or '-')
                continue;
            sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }

    public static bool FingerprintsEqual(string? a, string? b) =>
        string.Equals(NormalizeFingerprint(a), NormalizeFingerprint(b), StringComparison.Ordinal);

    public static string ComputeCertificateFingerprintSha256(X509Certificate? certificate)
    {
        if (certificate == null)
            return "";
        var raw = certificate is X509Certificate2 c2 ? c2.RawData : certificate.GetRawCertData();
        return ToHexLower(Sha256(raw));
    }

    private static byte[] Sha256(byte[] data)
    {
#if NET5_0_OR_GREATER
        return SHA256.HashData(data);
#else
        using var sha = SHA256.Create();
        return sha.ComputeHash(data);
#endif
    }

    private static string ToHexLower(byte[] bytes)
    {
#if NET5_0_OR_GREATER
        return Convert.ToHexString(bytes).ToLowerInvariant();
#else
        var c = new char[bytes.Length * 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            var b = bytes[i];
            c[i * 2] = HexNibble((byte)(b >> 4));
            c[i * 2 + 1] = HexNibble((byte)(b & 0xF));
        }

        return new string(c);
#endif
    }

    private static char HexNibble(byte v) => (char)(v < 10 ? '0' + v : 'a' + (v - 10));

    private static bool ValidateCertificate(
        X509Certificate? cert,
        string? pinnedFingerprint,
        bool requirePin)
    {
        if (cert == null)
            return false;

        if (!requirePin || string.IsNullOrEmpty(pinnedFingerprint))
        {
            // Bootstrap / untrusted: allow self-signed; fingerprint will be pinned after GET /certificate.
            return true;
        }

        var actual = ComputeCertificateFingerprintSha256(cert);
        return FingerprintsEqual(actual, pinnedFingerprint);
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
            return default;
        _disposed = true;
        _session.Clear();
        _commandHttp.Dispose();
        _longPollHttp.Dispose();
        return default;
    }

    private sealed class ConnectionPinHolder
    {
        public string? PinnedFingerprintSha256 { get; set; }
        public bool RequirePin { get; set; }
    }
}
