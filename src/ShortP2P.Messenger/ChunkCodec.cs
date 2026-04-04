using System.Buffers.Binary;
using System.Security.Cryptography;
using ShortP2P.Crypto;

namespace ShortP2P.Messenger;

internal static class ChunkCodec
{
    /// <summary>16 байт Guid + 4 индекс + 4 всего.</summary>
    public const int HeaderBytes = 24;

    public static int MaxPayloadPerChunk(P2PSession session)
    {
        return P2PSession.MaxPlainTextBytes - HeaderBytes;
    }

    public static byte[] BuildChunk(Guid messageId, int chunkIndex, int totalChunks, ReadOnlySpan<byte> payloadSlice)
    {
        if (chunkIndex < 0) throw new ArgumentOutOfRangeException(nameof(chunkIndex));
        if (totalChunks <= 0) throw new ArgumentOutOfRangeException(nameof(totalChunks));
        if (chunkIndex >= totalChunks) throw new ArgumentOutOfRangeException(nameof(chunkIndex));

        var plain = new byte[HeaderBytes + payloadSlice.Length];
        if (!messageId.TryWriteBytes(plain.AsSpan(0, 16)))
            throw new InvalidOperationException("Failed to write Guid.");
        BinaryPrimitives.WriteInt32BigEndian(plain.AsSpan(16), chunkIndex);
        BinaryPrimitives.WriteInt32BigEndian(plain.AsSpan(20), totalChunks);
        payloadSlice.CopyTo(plain.AsSpan(HeaderBytes));
        return plain;
    }

    public static void ParseChunk(ReadOnlySpan<byte> plaintext, out Guid messageId, out int chunkIndex,
        out int totalChunks, out ReadOnlySpan<byte> payload)
    {
        if (plaintext.Length < HeaderBytes) throw new CryptographicException("Chunk plaintext is too short.");
        messageId = new Guid(plaintext.Slice(0, 16));
        chunkIndex = BinaryPrimitives.ReadInt32BigEndian(plaintext.Slice(16, 4));
        totalChunks = BinaryPrimitives.ReadInt32BigEndian(plaintext.Slice(20, 4));
        payload = plaintext.Slice(HeaderBytes);
        if (totalChunks <= 0 || chunkIndex < 0 || chunkIndex >= totalChunks)
            throw new CryptographicException("Invalid chunk header.");
    }
}