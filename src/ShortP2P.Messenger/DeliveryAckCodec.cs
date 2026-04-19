namespace ShortP2P.Messenger;

/// <summary>Внутренний plaintext: квитанция доставки полного сообщения (после сборки чанков).</summary>
internal static class DeliveryAckCodec
{
    private const byte Marker = 0xAC;

    public static byte[] ToBytes(Guid messageId)
    {
        var b = new byte[17];
        b[0] = Marker;
        return !messageId.TryWriteBytes(b.AsSpan(1)) ? throw new InvalidOperationException("Guid write failed.") : b;
    }

    public static bool TryParse(ReadOnlySpan<byte> plaintext, out Guid messageId)
    {
        messageId = Guid.Empty;
        if (plaintext.Length != 17 || plaintext[0] != Marker)
            return false;
        messageId = new Guid(plaintext[1..]);
        return true;
    }
}
