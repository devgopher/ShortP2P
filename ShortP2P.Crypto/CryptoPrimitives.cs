using System;

namespace ShortP2P.Crypto
{
    internal static class CryptoPrimitives
    {
        public static byte[] Concat(params byte[][] arrays)
        {
            if (arrays == null) throw new ArgumentNullException(nameof(arrays));

            int total = 0;
            for (int i = 0; i < arrays.Length; i++)
            {
                if (arrays[i] == null) throw new ArgumentNullException(nameof(arrays), "Null array is not allowed.");
                total += arrays[i].Length;
            }

            byte[] result = new byte[total];
            int offset = 0;
            for (int i = 0; i < arrays.Length; i++)
            {
                Buffer.BlockCopy(arrays[i], 0, result, offset, arrays[i].Length);
                offset += arrays[i].Length;
            }
            return result;
        }

        public static bool ConstantTimeEquals(byte[] a, byte[] b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));
            if (a.Length != b.Length) return false;

            int diff = 0;
            for (int i = 0; i < a.Length; i++)
                diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }
}

