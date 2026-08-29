namespace ShortP2P.Crypto;

/// <summary>
///     RSA key pair for handshake.
/// </summary>
public sealed class RsaKeyPair
{
    internal RsaKeyPair(RsaPublicKey publicKey, RsaPrivateKey privateKey)
    {
        PublicKey = publicKey ?? throw new global::System.ArgumentNullException(nameof(publicKey));
        PrivateKey = privateKey ?? throw new global::System.ArgumentNullException(nameof(privateKey));
    }

    public RsaPublicKey PublicKey { get; }
    public RsaPrivateKey PrivateKey { get; }
}