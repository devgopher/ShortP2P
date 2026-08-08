namespace ShortP2P.MessengerServer.Domain;

/// <summary>Public key material associated with a directed peer pair.</summary>
public sealed class CryptoKeys
{
    public required string SrcNetworkId { get; init; }

    public required string TgtNetworkId { get; init; }

    /// <summary>Opaque public key string (e.g. RSA public JSON).</summary>
    public required string PublicKey { get; init; }
}
