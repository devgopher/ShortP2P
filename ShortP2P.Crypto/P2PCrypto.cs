using System.Security.Cryptography;

namespace ShortP2P.Crypto;

/// <summary>
///     Public facade for P2P key generation, handshake, and packet encryption.
/// </summary>
public static class P2PCrypto
{
    /// <summary>
    ///     Generates an RSA-1024 key pair for handshake.
    ///     PublicKey/PrivateKey are returned as raw RSA parameters.
    /// </summary>
    public static RsaKeyPair GenerateKeyPair()
    {
        // RSA key generation uses platform crypto.
        using var rsa = RSA.Create();
        rsa.KeySize = 1024;
        var parameters = rsa.ExportParameters(true);
        var pub = new RsaPublicKey(parameters.Modulus, parameters.Exponent);
        var priv = new RsaPrivateKey(parameters);
        return new RsaKeyPair(pub, priv);
    }

    /// <summary>
    ///     Creates a handshake packet for the peer.
    ///     The caller must provide the peer's public key.
    /// </summary>
    public static byte[] CreateHandshake(RsaPublicKey remotePublicKey)
    {
        return P2PHandshake.CreateHandshakePacket(remotePublicKey);
    }

    /// <summary>
    ///     Starts the handshake from the initiator side: returns the packet to send to the peer
    ///     and a ready-to-use session for encrypting packets.
    /// </summary>
    public static P2PHandshakeResult CreateHandshakeInitiation(RsaPublicKey remotePublicKey)
    {
        return P2PHandshake.CreateHandshakeInitiation(remotePublicKey);
    }

    /// <summary>
    ///     Creates a session from peer handshake packet by decrypting it with the local private key.
    /// </summary>
    public static P2PSession CreateSession(RsaPrivateKey localPrivateKey, byte[] remoteHandshakePacket)
    {
        Require.NotNull(localPrivateKey);
        Require.NotNull(remoteHandshakePacket);

        var sessionKeys = P2PHandshake.DecryptHandshakePacket(localPrivateKey, remoteHandshakePacket);

        var aesKey = new byte[16];
        var macKey = new byte[32];
        Buffer.BlockCopy(sessionKeys, 0, aesKey, 0, 16);
        Buffer.BlockCopy(sessionKeys, 16, macKey, 0, 32);

        return new P2PSession(aesKey, macKey);
    }

    /// <summary>
    ///     Convenience wrapper for <see cref="P2PSession.Encrypt(byte[])" />.
    /// </summary>
    public static byte[] Encrypt(P2PSession session, byte[] plaintext)
    {
        Require.NotNull(session);
        return session.Encrypt(plaintext);
    }

    /// <summary>
    ///     Convenience wrapper for <see cref="P2PSession.Decrypt(byte[])" />.
    /// </summary>
    public static byte[] Decrypt(P2PSession session, byte[] packet)
    {
        Require.NotNull(session);
        return session.Decrypt(packet);
    }
}