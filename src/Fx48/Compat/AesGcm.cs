#if NETFRAMEWORK
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;

namespace System.Security.Cryptography;

/// <summary>AES-GCM polyfill for net48 (same envelope as .NET AesGcm).</summary>
internal sealed class AesGcm : IDisposable
{
    private readonly byte[] _key;
    private readonly int _tagSizeBytes;
    private bool _disposed;

    public AesGcm(byte[] key, int tagSizeInBytes)
    {
        if (key == null)
            throw new global::System.ArgumentNullException(nameof(key));
        if (tagSizeInBytes < 12 || tagSizeInBytes > 16)
            throw new global::System.ArgumentException("AES-GCM tag must be 12..16 bytes.", nameof(tagSizeInBytes));
        _key = (byte[])key.Clone();
        _tagSizeBytes = tagSizeInBytes;
    }

    public void Encrypt(
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> plaintext,
        Span<byte> ciphertext,
        Span<byte> tag)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AesGcm));
        if (ciphertext.Length != plaintext.Length)
            throw new global::System.ArgumentException("Ciphertext length must match plaintext.");
        if (tag.Length != _tagSizeBytes)
            throw new global::System.ArgumentException("Tag length mismatch.", nameof(tag));

        var output = Process(true, nonce, plaintext);
        if (output.Length != plaintext.Length + _tagSizeBytes)
            throw new CryptographicException("Unexpected AES-GCM encrypt output length.");
        output.AsSpan(0, plaintext.Length).CopyTo(ciphertext);
        output.AsSpan(plaintext.Length, _tagSizeBytes).CopyTo(tag);
    }

    public void Decrypt(
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> ciphertext,
        ReadOnlySpan<byte> tag,
        Span<byte> plaintext)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AesGcm));
        if (plaintext.Length != ciphertext.Length)
            throw new global::System.ArgumentException("Plaintext length must match ciphertext.");
        if (tag.Length != _tagSizeBytes)
            throw new global::System.ArgumentException("Tag length mismatch.", nameof(tag));

        var packed = new byte[ciphertext.Length + tag.Length];
        ciphertext.CopyTo(packed);
        tag.CopyTo(packed.AsSpan(ciphertext.Length));
        var output = Process(false, nonce, packed);
        if (output.Length != plaintext.Length)
            throw new CryptographicException("Unexpected AES-GCM decrypt output length.");
        output.CopyTo(plaintext);
    }

    private byte[] Process(bool forEncryption, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> input)
    {
        var cipher = new GcmBlockCipher(new AesEngine());
        cipher.Init(forEncryption, new AeadParameters(new KeyParameter(_key), _tagSizeBytes * 8, nonce.ToArray()));
        var output = new byte[cipher.GetOutputSize(input.Length)];
        var len = cipher.ProcessBytes(input.ToArray(), 0, input.Length, output, 0);
        len += cipher.DoFinal(output, len);
        if (len == output.Length)
            return output;
        var trimmed = new byte[len];
        Buffer.BlockCopy(output, 0, trimmed, 0, len);
        return trimmed;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        Array.Clear(_key, 0, _key.Length);
        _disposed = true;
    }
}
#endif
