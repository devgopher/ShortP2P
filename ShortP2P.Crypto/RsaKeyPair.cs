using System;

namespace ShortP2P.Crypto
{
    /// <summary>
    /// RSA key pair for handshake.
    /// </summary>
    public sealed class RsaKeyPair
    {
        public RsaPublicKey PublicKey { get; }
        public RsaPrivateKey PrivateKey { get; }

        internal RsaKeyPair(RsaPublicKey publicKey, RsaPrivateKey privateKey)
        {
            PublicKey = publicKey ?? throw new ArgumentNullException(nameof(publicKey));
            PrivateKey = privateKey ?? throw new ArgumentNullException(nameof(privateKey));
        }
    }
}

