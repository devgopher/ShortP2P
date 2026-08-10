using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ShortP2P.MessengerServer.UseCases.Abstractions;

namespace ShortP2P.MessengerServer.Api.Auth;

/// <summary>
/// Reads the ASP.NET Core HTTPS development certificate (or another CurrentUser My cert)
/// to expose a SHA-256 fingerprint for client pinning.
/// </summary>
public sealed class KestrelServerCertificateReader(ILogger<KestrelServerCertificateReader> logger)
    : IServerCertificateReader
{
    /// <summary>OID of the ASP.NET Core HTTPS development certificate extension.</summary>
    private const string AspNetHttpsDevCertOid = "1.3.6.1.4.1.311.84.1.1";

    public Task<ServerCertificateInfo> GetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var certificate = TryResolveCertificate();
        if (certificate is null)
        {
            logger.LogWarning("No HTTPS server certificate is available for fingerprint.");
            return Task.FromResult(new ServerCertificateInfo(
                FingerprintSha256: string.Empty,
                Subject: null,
                NotAfterUtc: null));
        }

        var hash = SHA256.HashData(certificate.RawData);
        return Task.FromResult(new ServerCertificateInfo(
            FingerprintSha256: Convert.ToHexString(hash),
            Subject: certificate.Subject,
            NotAfterUtc: certificate.NotAfter.ToUniversalTime()));
    }

    private X509Certificate2? TryResolveCertificate()
    {
        try
        {
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly);

            var candidates = store.Certificates
                .Where(c => c.HasPrivateKey)
                .OrderByDescending(c => c.NotAfter)
                .ToList();

            var aspNetDev = candidates.FirstOrDefault(c =>
                c.Extensions.Any(e => e.Oid?.Value == AspNetHttpsDevCertOid));
            if (aspNetDev is not null)
                return new X509Certificate2(aspNetDev);

            var localhost = candidates.FirstOrDefault(c =>
                c.Subject.Contains("CN=localhost", StringComparison.OrdinalIgnoreCase));
            if (localhost is not null)
                return new X509Certificate2(localhost);

            var any = candidates.FirstOrDefault(c => c.NotAfter > DateTime.Now);
            return any is null ? null : new X509Certificate2(any);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to resolve server certificate from CurrentUser\\My store.");
            return null;
        }
    }
}
