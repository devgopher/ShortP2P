using System.Security.Cryptography;
using ShortP2P.Crypto;

namespace ShortP2P.Client.Services.MessengerServers;

/// <summary>
/// Hybrid RSA-OAEP + AES-GCM envelope for store-and-forward server messages (opaque to the server).
/// Format: version(1) | keyLen(2 BE) | rsa(aesKey+nonce) | ciphertext+tag
/// </summary>
public static class MessengerServerPayloadCodec
{
    private const byte Version = 1;
    private const int AesKeyBytes = 32;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;

    public static string EncryptToBase64(ReadOnlySpan<byte> plaintext, RsaPublicKey recipientPublicKey)
    {
        ArgumentNullException.ThrowIfNull(recipientPublicKey);

        var aesKey = RandomNumberGenerator.GetBytes(AesKeyBytes);
        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagBytes];
        using (var aes = new AesGcm(aesKey, TagBytes))
            aes.Encrypt(nonce, plaintext, ciphertext, tag);

        var keyMaterial = new byte[AesKeyBytes + NonceBytes];
        Buffer.BlockCopy(aesKey, 0, keyMaterial, 0, AesKeyBytes);
        Buffer.BlockCopy(nonce, 0, keyMaterial, AesKeyBytes, NonceBytes);

        byte[] wrappedKey;
        using (var rsa = RSA.Create())
        {
            rsa.ImportParameters(new RSAParameters
            {
                Modulus = recipientPublicKey.Modulus,
                Exponent = recipientPublicKey.Exponent
            });
            wrappedKey = rsa.Encrypt(keyMaterial, RSAEncryptionPadding.OaepSHA256);
        }

        CryptographicOperations.ZeroMemory(aesKey);
        CryptographicOperations.ZeroMemory(keyMaterial);

        var result = new byte[1 + 2 + wrappedKey.Length + ciphertext.Length + TagBytes];
        result[0] = Version;
        result[1] = (byte)(wrappedKey.Length >> 8);
        result[2] = (byte)wrappedKey.Length;
        Buffer.BlockCopy(wrappedKey, 0, result, 3, wrappedKey.Length);
        var bodyOffset = 3 + wrappedKey.Length;
        Buffer.BlockCopy(ciphertext, 0, result, bodyOffset, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, result, bodyOffset + ciphertext.Length, TagBytes);
        return Convert.ToBase64String(result);
    }

    public static byte[] DecryptFromBase64(string encryptedDataBase64, RsaPrivateKey privateKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(encryptedDataBase64);
        ArgumentNullException.ThrowIfNull(privateKey);

        var blob = Convert.FromBase64String(encryptedDataBase64.Trim());
        if (blob.Length < 1 + 2 + 1 + TagBytes)
            throw new CryptographicException("Server payload is too short.");
        if (blob[0] != Version)
            throw new CryptographicException($"Unsupported server payload version: {blob[0]}.");

        var keyLen = (blob[1] << 8) | blob[2];
        if (keyLen <= 0 || 3 + keyLen + TagBytes > blob.Length)
            throw new CryptographicException("Invalid server payload key length.");

        var wrappedKey = blob.AsSpan(3, keyLen).ToArray();
        var cipherAndTag = blob.AsSpan(3 + keyLen);
        var cipherLen = cipherAndTag.Length - TagBytes;
        if (cipherLen < 0)
            throw new CryptographicException("Invalid server payload ciphertext.");

        var ciphertext = cipherAndTag[..cipherLen].ToArray();
        var tag = cipherAndTag[cipherLen..].ToArray();

        byte[] keyMaterial;
        using (var rsa = RSA.Create())
        {
            rsa.ImportParameters(new RSAParameters
            {
                Modulus = privateKey.Modulus,
                Exponent = privateKey.Exponent,
                D = privateKey.D,
                P = privateKey.P,
                Q = privateKey.Q,
                DP = privateKey.DP,
                DQ = privateKey.DQ,
                InverseQ = privateKey.InverseQ
            });
            keyMaterial = rsa.Decrypt(wrappedKey, RSAEncryptionPadding.OaepSHA256);
        }

        if (keyMaterial.Length != AesKeyBytes + NonceBytes)
            throw new CryptographicException("Invalid unwrapped key material.");

        var aesKey = keyMaterial.AsSpan(0, AesKeyBytes);
        var nonce = keyMaterial.AsSpan(AesKeyBytes, NonceBytes);
        var plaintext = new byte[cipherLen];
        using (var aes = new AesGcm(aesKey.ToArray(), TagBytes))
            aes.Decrypt(nonce, ciphertext, tag, plaintext);

        CryptographicOperations.ZeroMemory(keyMaterial);
        return plaintext;
    }
}
