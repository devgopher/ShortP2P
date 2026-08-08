namespace ShortP2P.MessengerServer.Contracts.Dtos;

/// <summary>Server TLS certificate info for client pinning.</summary>
public sealed class ServerCertificateResponse
{
    /// <summary>SHA-256 fingerprint, typically hex (lowercase, no separators) or colon-separated.</summary>
    public required string FingerprintSha256 { get; init; }

    public string? Subject { get; init; }

    public DateTime? NotAfterUtc { get; init; }
}
