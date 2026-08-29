using System.Security.Cryptography;

namespace ShortP2P.Crypto;

/// <summary>
///     RSA public key parameters (Modulus + Exponent).
/// </summary>
public sealed class RsaPublicKey
{
    public RsaPublicKey(byte[] modulus, byte[] exponent)
    {
        Require.NotNull(modulus);
        Require.NotNull(exponent);
        if (modulus.Length == 0) throw new global::System.ArgumentException("Modulus is empty.", nameof(modulus));
        if (exponent.Length == 0) throw new global::System.ArgumentException("Exponent is empty.", nameof(exponent));

        Modulus = (byte[])modulus.Clone();
        Exponent = (byte[])exponent.Clone();
    }

    public byte[] Modulus { get; }
    public byte[] Exponent { get; }

    internal RSAParameters ToParameters()
    {
        return new RSAParameters
        {
            Modulus = (byte[])Modulus.Clone(),
            Exponent = (byte[])Exponent.Clone()
        };
    }
}