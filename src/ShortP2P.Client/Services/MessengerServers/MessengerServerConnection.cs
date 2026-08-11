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
    private readonly HttpClient _httpClient;
    private readonly MessengerServerSession _session;
    private readonly ConnectionPinHolder _pinHolder;
    private bool _disposed;

    private MessengerServerConnection(
        MessengerServerEntity entity,
        HttpClient httpClient,
        MessengerServerSession session,
        MessengerServerApiClient api,
        ConnectionPinHolder pinHolder)
    {
        Entity = entity;
        _httpClient = httpClient;
        _session = session;
        Api = api;
        _pinHolder = pinHolder;
    }

    public MessengerServerEntity Entity { get; private set; }

    public IMessengerServerApi Api { get; }

    public bool HasValidToken => _session.HasValidToken;

    public static MessengerServerConnection Create(MessengerServerEntity entity, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var session = new MessengerServerSession();
        var pinHolder = new ConnectionPinHolder
        {
            PinnedFingerprintSha256 = NormalizeFingerprint(entity.FingerprintSha256),
            RequirePin = entity.Trusted && !string.IsNullOrWhiteSpace(entity.FingerprintSha256)
        };

        var sockets = new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, cert, _, _) =>
                    ValidateCertificate(cert, pinHolder.PinnedFingerprintSha256, pinHolder.RequirePin)
            }
        };

        var http = new HttpClient(new MessengerServerBearerHandler(session) { InnerHandler = sockets })
        {
            BaseAddress = new Uri(SqliteMessengerServerRepository.NormalizeBaseUrl(entity.BaseUrl) + "/"),
            Timeout = timeout
        };
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json");

        var api = new MessengerServerApiClient(http, session);
        return new MessengerServerConnection(entity, http, session, api, pinHolder);
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
        ArgumentNullException.ThrowIfNull(entity);
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
        return Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant();
    }

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
            return ValueTask.CompletedTask;
        _disposed = true;
        _session.Clear();
        _httpClient.Dispose();
        return ValueTask.CompletedTask;
    }

    private sealed class ConnectionPinHolder
    {
        public string? PinnedFingerprintSha256 { get; set; }
        public bool RequirePin { get; set; }
    }
}
