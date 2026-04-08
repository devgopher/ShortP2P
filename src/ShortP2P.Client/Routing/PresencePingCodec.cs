namespace ShortP2P.Client.Routing;

/// <summary>
///     Кодек ping-пакета присутствия: "я онлайн".
/// </summary>
public static class PresencePingCodec
{
    public const byte FramePresencePing = 0x31;

    public static byte[] Build(Guid networkId)
    {
        var buf = new byte[17];
        buf[0] = FramePresencePing;
        networkId.TryWriteBytes(buf.AsSpan(1, 16));
        return buf;
    }

    public static bool TryParse(ReadOnlySpan<byte> datagram, out Guid networkId)
    {
        networkId = Guid.Empty;
        if (datagram.Length != 17 || datagram[0] != FramePresencePing)
            return false;
        networkId = new Guid(datagram.Slice(1, 16));
        return true;
    }
}
