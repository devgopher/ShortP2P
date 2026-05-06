using System;
using System.Security.Cryptography;

namespace ShortP2P.Crypto
{
    /// <summary>
    ///     Established session keys derived from ECDH.
    ///     Provides packet encryption/decryption (AES-CBC + HMAC-SHA256 truncated).
    ///     Encrypted packet size is limited to <= 128 bytes.
    /// </summary>
    public sealed class P2PSession
    {
        private const int MaxEncryptedPacketBytes = 128;
        private const int IvBytes = 16;
        private const int TagBytes = 16; // truncated HMAC
        private const int AesBlockBytes = 16;

        private readonly byte[] _aesKey; // 16 bytes (AES-128)
        private readonly byte[] _macKey; // 32 bytes (for HMAC-SHA256)

        internal P2PSession(byte[] aesKey, byte[] macKey)
        {
            ArgumentNullException.ThrowIfNull(aesKey);
            ArgumentNullException.ThrowIfNull(macKey);
            if (aesKey.Length != 16) throw new ArgumentException("aesKey must be 16 bytes (AES-128).", nameof(aesKey));
            if (macKey.Length != 32) throw new ArgumentException("macKey must be 32 bytes.", nameof(macKey));

            _aesKey = (byte[])aesKey.Clone();
            _macKey = (byte[])macKey.Clone();
        }

        /// <summary>
        ///     Maximum plaintext length that guarantees encrypted packet size &lt;= 128 bytes.
        /// </summary>
        public int MaxPlaintextBytes
        {
            get
            {
                // packet = IV(16) + ciphertext(padded to 16) + tag(16) <= 128
                // => paddedLen <= 96; PKCS7 adds at least one block => plaintextLen <= 95
                return 95;
            }
        }

        public byte[] Encrypt(byte[] plaintext)
        {
            ArgumentNullException.ThrowIfNull(plaintext);
            if (plaintext.Length > MaxPlaintextBytes)
                throw new ArgumentException(
                    $"Plaintext is too large. Max is {MaxPlaintextBytes} bytes for <= {MaxEncryptedPacketBytes}-byte packets.",
                    nameof(plaintext));

            var iv = new byte[IvBytes];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(iv);
            }

            byte[] ciphertext;
            using (var aes = Aes.Create())
            {
                aes.KeySize = 128;
                aes.Key = _aesKey;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using (var encryptor = aes.CreateEncryptor())
                {
                    ciphertext = encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);
                }
            }

            var packetLen = IvBytes + ciphertext.Length + TagBytes;
            if (packetLen > MaxEncryptedPacketBytes)
                throw new CryptographicException(
                    $"Encrypted packet would be {packetLen} bytes, which exceeds the limit of {MaxEncryptedPacketBytes} bytes.");

            var tag = ComputeTag(iv, ciphertext);

            var packet = new byte[packetLen];
            Buffer.BlockCopy(iv, 0, packet, 0, IvBytes);
            Buffer.BlockCopy(ciphertext, 0, packet, IvBytes, ciphertext.Length);
            Buffer.BlockCopy(tag, 0, packet, IvBytes + ciphertext.Length, TagBytes);

            return packet;
        }

        public byte[] Decrypt(byte[] packet)
        {
            ArgumentNullException.ThrowIfNull(packet);
            if (packet.Length > MaxEncryptedPacketBytes)
                throw new ArgumentException($"Packet length exceeds limit {MaxEncryptedPacketBytes} bytes.",
                    nameof(packet));
            if (packet.Length < IvBytes + TagBytes)
                throw new ArgumentException("Packet is too short.", nameof(packet));

            var ciphertextLen = packet.Length - IvBytes - TagBytes;
            if (ciphertextLen <= 0)
                throw new ArgumentException("Packet ciphertext length is invalid.", nameof(packet));
            if (ciphertextLen % AesBlockBytes != 0)
                throw new ArgumentException(
                    $"Invalid ciphertext length: {ciphertextLen} (must be multiple of {AesBlockBytes}).",
                    nameof(packet));

            var iv = new byte[IvBytes];
            Buffer.BlockCopy(packet, 0, iv, 0, IvBytes);

            var ciphertext = new byte[ciphertextLen];
            Buffer.BlockCopy(packet, IvBytes, ciphertext, 0, ciphertextLen);

            var receivedTag = new byte[TagBytes];
            Buffer.BlockCopy(packet, IvBytes + ciphertextLen, receivedTag, 0, TagBytes);

            var expectedTag = ComputeTag(iv, ciphertext);
            if (!CryptoPrimitives.ConstantTimeEquals(expectedTag, receivedTag))
                throw new CryptographicException("Invalid packet authentication tag.");

            using (var aes = Aes.Create())
            {
                aes.KeySize = 128;
                aes.Key = _aesKey;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using (var decryptor = aes.CreateDecryptor())
                {
                    return decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
                }
            }
        }

        private byte[] ComputeTag(byte[] iv, byte[] ciphertext)
        {
            var input = CryptoPrimitives.Concat(iv, ciphertext);
            using (var hmac = new HMACSHA256(_macKey))
            {
                var full = hmac.ComputeHash(input);
                var tag = new byte[TagBytes];
                Buffer.BlockCopy(full, 0, tag, 0, TagBytes);

                return tag;
            }
        }
    }
}