using System;
using System.Security.Cryptography;

namespace ShortP2P.Crypto
{
    /// <summary>
    /// RSA private key parameters.
    /// </summary>
    public sealed class RsaPrivateKey
    {
        public byte[] Modulus { get; }
        public byte[] Exponent { get; }
        public byte[] D { get; }
        public byte[] P { get; }
        public byte[] Q { get; }
        public byte[] DP { get; }
        public byte[] DQ { get; }
        public byte[] InverseQ { get; }

        internal RsaPrivateKey(RSAParameters parameters)
        {
            if (parameters.Modulus == null || parameters.Modulus.Length == 0) throw new ArgumentException("Invalid Modulus.", nameof(parameters));
            if (parameters.Exponent == null || parameters.Exponent.Length == 0) throw new ArgumentException("Invalid Exponent.", nameof(parameters));
            if (parameters.D == null || parameters.D.Length == 0) throw new ArgumentException("Invalid D.", nameof(parameters));
            if (parameters.P == null || parameters.P.Length == 0) throw new ArgumentException("Invalid P.", nameof(parameters));
            if (parameters.Q == null || parameters.Q.Length == 0) throw new ArgumentException("Invalid Q.", nameof(parameters));
            if (parameters.DP == null || parameters.DP.Length == 0) throw new ArgumentException("Invalid DP.", nameof(parameters));
            if (parameters.DQ == null || parameters.DQ.Length == 0) throw new ArgumentException("Invalid DQ.", nameof(parameters));
            if (parameters.InverseQ == null || parameters.InverseQ.Length == 0) throw new ArgumentException("Invalid InverseQ.", nameof(parameters));

            Modulus = (byte[])parameters.Modulus.Clone();
            Exponent = (byte[])parameters.Exponent.Clone();
            D = (byte[])parameters.D.Clone();
            P = (byte[])parameters.P.Clone();
            Q = (byte[])parameters.Q.Clone();
            DP = (byte[])parameters.DP.Clone();
            DQ = (byte[])parameters.DQ.Clone();
            InverseQ = (byte[])parameters.InverseQ.Clone();
        }

        internal RSAParameters ToParameters()
        {
            return new RSAParameters
            {
                Modulus = (byte[])Modulus.Clone(),
                Exponent = (byte[])Exponent.Clone(),
                D = (byte[])D.Clone(),
                P = (byte[])P.Clone(),
                Q = (byte[])Q.Clone(),
                DP = (byte[])DP.Clone(),
                DQ = (byte[])DQ.Clone(),
                InverseQ = (byte[])InverseQ.Clone(),
            };
        }

        internal RsaPublicKey ToPublicKey()
        {
            return new RsaPublicKey(Modulus, Exponent);
        }
    }
}

