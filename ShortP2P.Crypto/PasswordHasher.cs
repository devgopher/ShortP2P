using System.Security.Cryptography;

namespace ShortP2P.Crypto;

public static class PasswordHasher
{
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 120_000;

    public static (string SaltBase64, string HashBase64) Hash(string password)
    {
        var salt = GetRandomBytes(SaltSize);
        var hash = Pbkdf2(password, salt, KeySize);
        return (Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    public static bool Verify(string password, string saltBase64, string hashBase64)
    {
        var salt = Convert.FromBase64String(saltBase64);
        var expected = Convert.FromBase64String(hashBase64);
        var actual = Pbkdf2(password, salt, expected.Length);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private static byte[] GetRandomBytes(int count)
    {
#if NET6_0_OR_GREATER
        return RandomNumberGenerator.GetBytes(count);
#else
        var bytes = new byte[count];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return bytes;
#endif
    }

    private static byte[] Pbkdf2(string password, byte[] salt, int keySize)
    {
#if NET6_0_OR_GREATER
        return Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, keySize);
#else
        using var kdf = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256);
        return kdf.GetBytes(keySize);
#endif
    }
}