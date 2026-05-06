using System;

namespace ShortP2P.Crypto
{
    internal static class CryptoPrimitives
    {
        public static byte[] Concat(params byte[][] arrays)
        {
            ArgumentNullException.ThrowIfNull(arrays);

            var total = 0;
            for (var i = 0; i < arrays.Length; i++)
            {
                if (arrays[i] == null) throw new ArgumentNullException(nameof(arrays), "Null array is not allowed.");
                total += arrays[i].Length;
            }

            var result = new byte[total];
            var offset = 0;
            for (var i = 0; i < arrays.Length; i++)
            {
                Buffer.BlockCopy(arrays[i], 0, result, offset, arrays[i].Length);
                offset += arrays[i].Length;
            }

            return result;
        }

        public static bool ConstantTimeEquals(byte[] a, byte[] b)
        {
            ArgumentNullException.ThrowIfNull(a);
            ArgumentNullException.ThrowIfNull(b);
            if (a.Length != b.Length) return false;

            var diff = 0;
            for (var i = 0; i < a.Length; i++)
                diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }
}