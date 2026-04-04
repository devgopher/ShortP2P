using System.Security.Cryptography;

namespace ShortP2P.Crypto.Tests;

public class P2PCryptoTests
{
    [Fact]
    public void GenerateKeyPair_ShouldReturnValidKeys()
    {
        var kp = P2PCrypto.GenerateKeyPair();

        Assert.NotNull(kp);
        Assert.NotNull(kp.PublicKey);
        Assert.NotNull(kp.PrivateKey);

        // RSA-1024 => modulus is 128 bytes.
        Assert.NotNull(kp.PublicKey.Modulus);
        Assert.Equal(128, kp.PublicKey.Modulus.Length);
        Assert.NotNull(kp.PublicKey.Exponent);
        Assert.InRange(kp.PublicKey.Exponent.Length, 1, 4);

        Assert.NotNull(kp.PrivateKey.Modulus);
        Assert.Equal(128, kp.PrivateKey.Modulus.Length);
        Assert.NotNull(kp.PrivateKey.D);
        Assert.NotNull(kp.PrivateKey.P);
        Assert.NotNull(kp.PrivateKey.Q);
        Assert.NotNull(kp.PrivateKey.DP);
        Assert.NotNull(kp.PrivateKey.DQ);
        Assert.NotNull(kp.PrivateKey.InverseQ);
    }

    [Fact]
    public void EncryptDecrypt_ShouldRoundTrip_ForDifferentPlaintextSizes()
    {
        var bobKeys = P2PCrypto.GenerateKeyPair();

        var hs = P2PCrypto.CreateHandshakeInitiation(bobKeys.PublicKey);
        var aliceSession = hs.Session;
        var bobSession = P2PCrypto.CreateSession(bobKeys.PrivateKey, hs.HandshakePacket);

        Assert.Equal(128, hs.HandshakePacket.Length);
        Assert.NotNull(aliceSession);
        Assert.NotNull(bobSession);

        var sizes = new List<int> { 0, 1, 15, P2PSession.MaxPlainTextBytes };
        foreach (var size in sizes)
        {
            var plaintext = RandomBytes(size);
            var encrypted = aliceSession.Encrypt(plaintext);
            Assert.True(encrypted.Length <= 128, $"Encrypted packet len={encrypted.Length}, plaintext len={size}");

            var decrypted = bobSession.Decrypt(encrypted);
            Assert.True(plaintext.SequenceEqual(decrypted), $"Mismatch at plaintext len={size}");
        }
    }

    [Fact]
    public void Decrypt_TamperedPacket_ShouldThrowCryptographicException()
    {
        var bobKeys = P2PCrypto.GenerateKeyPair();

        var hs = P2PCrypto.CreateHandshakeInitiation(bobKeys.PublicKey);
        var session = P2PCrypto.CreateSession(bobKeys.PrivateKey, hs.HandshakePacket);

        var plaintext = RandomBytes(32);
        var encrypted = hs.Session.Encrypt(plaintext);

        Assert.True(encrypted.Length <= 128);

        var tampered = (byte[])encrypted.Clone();
        tampered[^1] ^= 0x01;

        Assert.Throws<CryptographicException>(() => session.Decrypt(tampered));
    }

    [Fact]
    public void Encrypt_TooLargePlaintext_ShouldThrowArgumentException()
    {
        var bobKeys = P2PCrypto.GenerateKeyPair();

        var hs = P2PCrypto.CreateHandshakeInitiation(bobKeys.PublicKey);
        var session = hs.Session;

        var tooLarge = new byte[session.MaxPlaintextBytes + 1];

        Assert.Throws<ArgumentException>(() => session.Encrypt(tooLarge));
    }

    private static byte[] RandomBytes(int len)
    {
        switch (len)
        {
            case < 0:
                throw new ArgumentOutOfRangeException(nameof(len));
            case 0:
                return [];
        }

        var data = new byte[len];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(data);

        return data;
    }
}