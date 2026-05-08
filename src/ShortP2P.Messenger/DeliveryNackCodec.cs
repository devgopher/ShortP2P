namespace ShortP2P.Messenger;

/// <summary>
///     Внутренний plaintext: запрос дозапроса отсутствующих чанков сообщения.
///     Формат: marker(1) + messageId(16) + count(2, BE) + indices[count] (int32 BE).
/// </summary>
internal static class DeliveryNackCodec
{
    private const byte Marker = 0xAD;

    public static byte[] ToBytes(Guid messageId, ReadOnlySpan<int> missingChunkIndices)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(missingChunkIndices.Length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(missingChunkIndices.Length, ushort.MaxValue);

        var len = 1 + 16 + 2 + missingChunkIndices.Length * 4;
        var packet = new byte[len];
        packet[0] = Marker;
        if (!messageId.TryWriteBytes(packet.AsSpan(1, 16)))
            throw new InvalidOperationException("Guid write failed.");

        packet[17] = (byte)(missingChunkIndices.Length >> 8);
        packet[18] = (byte)missingChunkIndices.Length;

        var pos = 19;
        for (var i = 0; i < missingChunkIndices.Length; i++)
        {
            var idx = missingChunkIndices[i];
            if (idx < 0)
                throw new ArgumentOutOfRangeException(nameof(missingChunkIndices), "Chunk index must be non-negative.");

            packet[pos++] = (byte)(idx >> 24);
            packet[pos++] = (byte)(idx >> 16);
            packet[pos++] = (byte)(idx >> 8);
            packet[pos++] = (byte)idx;
        }

        return packet;
    }

    public static bool TryParse(ReadOnlySpan<byte> plaintext, out Guid messageId, out int[] missingChunkIndices)
    {
        messageId = Guid.Empty;
        missingChunkIndices = [];
        if (plaintext.Length < 19 || plaintext[0] != Marker)
            return false;

        messageId = new Guid(plaintext.Slice(1, 16));
        var count = (ushort)((plaintext[17] << 8) | plaintext[18]);
        var expectedLen = 19 + count * 4;
        if (plaintext.Length != expectedLen)
            return false;

        var indices = new int[count];
        var pos = 19;
        for (var i = 0; i < count; i++)
        {
            var idx = (plaintext[pos] << 24) | (plaintext[pos + 1] << 16) | (plaintext[pos + 2] << 8) | plaintext[pos + 3];
            if (idx < 0)
                return false;
            indices[i] = idx;
            pos += 4;
        }

        missingChunkIndices = indices;
        return true;
    }
}
