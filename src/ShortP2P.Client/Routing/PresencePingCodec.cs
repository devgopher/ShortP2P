namespace ShortP2P.Client.Routing;

/// <summary>
///     Кодек ping-пакета присутствия: "я онлайн".
///     Датаграммы ходят только на выделенный UDP-порт <see cref="UdpPort" />, отдельно от основного data-порта.
/// </summary>
public static class PresencePingCodec
{
    /// <summary>Локальный и удалённый UDP-порт только для presence ping.</summary>
    public const int UdpPort = 565;

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
