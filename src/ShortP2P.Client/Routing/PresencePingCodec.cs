using System.Buffers.Binary;
using System.Text;

namespace ShortP2P.Client.Routing;

/// <summary>
///     Кодек discovery/presence ping на порту <see cref="UdpPort" />: network id + nickname (UTF-8) + порт data-UDP.
///     Формат: [0]=frame, [1..16]=Guid, [17..18]=длина ника, [19..]=UTF-8 ник, затем uint16 BE dataUdpPort.
///     Совместимость: 17 байт только Guid; 19+nick — ник без порта (порт по умолчанию <see cref="DefaultDataUdpPort" />).
/// </summary>
public static class PresencePingCodec
{
    /// <summary>Локальный и удалённый UDP-порт только для discovery/presence ping.</summary>
    public const int UdpPort = 565;

    public const byte FramePresencePing = 0x31;

    public const int MaxNicknameUtf8Bytes = 512;

    /// <summary>Если в пакете нет поля порта (старые клиенты).</summary>
    public const int DefaultDataUdpPort = 17200;

    public static byte[] Build(Guid networkId, string nickname, int dataUdpPort)
    {
        nickname ??= string.Empty;
        if (dataUdpPort is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(dataUdpPort));

        var nickBytes = Encoding.UTF8.GetBytes(nickname.Trim());
        if (nickBytes.Length > MaxNicknameUtf8Bytes)
            nickBytes = nickBytes.AsSpan(0, MaxNicknameUtf8Bytes).ToArray();

        var buf = new byte[1 + 16 + 2 + nickBytes.Length + 2];
        buf[0] = FramePresencePing;
        networkId.TryWriteBytes(buf.AsSpan(1, 16));
        BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(17, 2), (ushort)nickBytes.Length);
        nickBytes.CopyTo(buf.AsSpan(19));
        BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(19 + nickBytes.Length, 2), (ushort)dataUdpPort);
        return buf;
    }

    public static bool TryParse(ReadOnlySpan<byte> datagram, out Guid networkId, out string nickname,
        out int dataUdpPort)
    {
        networkId = Guid.Empty;
        nickname = "";
        dataUdpPort = DefaultDataUdpPort;

        if (datagram.Length < 17 || datagram[0] != FramePresencePing)
            return false;

        networkId = new Guid(datagram.Slice(1, 16));
        if (datagram.Length == 17)
            return true;

        if (datagram.Length < 19)
            return false;

        var nickLen = BinaryPrimitives.ReadUInt16BigEndian(datagram.Slice(17, 2));
        if (nickLen > MaxNicknameUtf8Bytes)
            return false;

        if (datagram.Length < 19 + nickLen)
            return false;

        try
        {
            nickname = Encoding.UTF8.GetString(datagram.Slice(19, nickLen));
        }
        catch
        {
            return false;
        }

        if (datagram.Length == 19 + nickLen)
            return true;

        if (datagram.Length != 19 + nickLen + 2)
            return false;

        dataUdpPort = BinaryPrimitives.ReadUInt16BigEndian(datagram.Slice(19 + nickLen, 2));
        return dataUdpPort is >= 1 and <= 65535;
    }
}
