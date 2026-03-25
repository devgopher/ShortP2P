using System;
using System.Security.Cryptography;

namespace ShortP2P.Crypto
{
    /// <summary>
    /// RSA-based handshake packaging.
    /// The initiator generates random session keys (AES key + HMAC key) and encrypts them with the peer's RSA public key.
    ///
    /// For RSA-1024, the encrypted handshake packet is exactly 128 bytes.
    /// </summary>
    public static class P2PHandshake
    {
        public const int MaxEncryptedPacketBytes = 128;

        private const int RsaKeySizeBits = 1024;
        private const int SessionKeyBytes = 16 + 32; // aesKey(16) + macKey(32)
        private const int HandshakePacketBytes = RsaKeySizeBits / 8; // 1024 / 8 = 128

        /// <summary>
        /// Creates handshake packet for the peer and returns an initiator-side session
        /// using the same randomly generated session keys.
        /// </summary>
        public static P2PHandshakeResult CreateHandshakeInitiation(RsaPublicKey remotePublicKey)
        {
            if (remotePublicKey == null) throw new ArgumentNullException(nameof(remotePublicKey));

            using (var rsa = RSA.Create())
            {
                rsa.ImportParameters(remotePublicKey.ToParameters());

                byte[] sessionKeys = new byte[SessionKeyBytes];
                using (var rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(sessionKeys);
                }

                byte[] encrypted = rsa.Encrypt(sessionKeys, RSAEncryptionPadding.OaepSHA1);
                if (encrypted.Length != HandshakePacketBytes)
                    throw new CryptographicException($"Unexpected handshake packet length: {encrypted.Length}. Expected {HandshakePacketBytes}.");

                var aesKey = new byte[16];
                var macKey = new byte[32];
                Buffer.BlockCopy(sessionKeys, 0, aesKey, 0, 16);
                Buffer.BlockCopy(sessionKeys, 16, macKey, 0, 32);

                var session = new P2PSession(aesKey, macKey);
                return new P2PHandshakeResult(encrypted, session);
            }
        }

        /// <summary>
        /// Creates handshake packet for the peer: RSA-OAEP(SHA1) encrypts (aesKey||macKey).
        /// </summary>
        public static byte[] CreateHandshakePacket(RsaPublicKey remotePublicKey)
        {
            return CreateHandshakeInitiation(remotePublicKey).HandshakePacket;
        }

        /// <summary>
        /// Decrypts handshake packet and returns (aesKey||macKey).
        /// </summary>
        public static byte[] DecryptHandshakePacket(RsaPrivateKey localPrivateKey, byte[] handshakePacket)
        {
            if (localPrivateKey == null) throw new ArgumentNullException(nameof(localPrivateKey));
            if (handshakePacket == null) throw new ArgumentNullException(nameof(handshakePacket));
            if (handshakePacket.Length != HandshakePacketBytes)
                throw new ArgumentException($"Handshake packet must be exactly {HandshakePacketBytes} bytes.", nameof(handshakePacket));

            using (var rsa = RSA.Create())
            {
                rsa.ImportParameters(localPrivateKey.ToParameters());
                byte[] decrypted = rsa.Decrypt(handshakePacket, RSAEncryptionPadding.OaepSHA1);
                if (decrypted.Length != SessionKeyBytes)
                    throw new CryptographicException($"Unexpected decrypted session key length: {decrypted.Length}. Expected {SessionKeyBytes}.");

                return decrypted;
            }
        }
    }
}

