using System.Buffers.Binary;
using System.Text;

namespace ShortP2P.Client.Routing;

/// <summary>
///     Кодек discovery/presence ping на порту <see cref="UdpPort" />: network id + nickname (UTF-8).
///     Формат: [0]=frame, [1..16]=Guid, [17..18]=uint16 BE длины ника, [19..]=UTF-8. Совместимость: 17 байт только Guid (ник пустой).
/// </summary>
public static class PresencePingCodec
{
    /// <summary>Локальный и удалённый UDP-порт только для discovery/presence ping.</summary>
    public const int UdpPort = 565;

    public const byte FramePresencePing = 0x31;

    public const int MaxNicknameUtf8Bytes = 512;

    public static byte[] Build(Guid networkId, string nickname)
    {
        nickname ??= string.Empty;
        var nickBytes = Encoding.UTF8.GetBytes(nickname.Trim());
        if (nickBytes.Length > MaxNicknameUtf8Bytes)
            nickBytes = nickBytes.AsSpan(0, MaxNicknameUtf8Bytes).ToArray();

        var buf = new byte[1 + 16 + 2 + nickBytes.Length];
        buf[0] = FramePresencePing;
        networkId.TryWriteBytes(buf.AsSpan(1, 16));
        BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(17, 2), (ushort)nickBytes.Length);
        nickBytes.CopyTo(buf.AsSpan(19));
        return buf;
    }

    public static bool TryParse(ReadOnlySpan<byte> datagram, out Guid networkId, out string nickname)
    {
        networkId = Guid.Empty;
        nickname = "";
        if (datagram.Length < 17 || datagram[0] != FramePresencePing)
            return false;

        networkId = new Guid(datagram.Slice(1, 16));
        if (datagram.Length == 17)
            return true;

        if (datagram.Length < 19)
            return false;

        var nickLen = BinaryPrimitives.ReadUInt16BigEndian(datagram.Slice(17, 2));
        if (nickLen > MaxNicknameUtf8Bytes || datagram.Length != 19 + nickLen)
            return false;

        try
        {
            nickname = Encoding.UTF8.GetString(datagram.Slice(19, nickLen));
        }
        catch
        {
            return false;
        }

        return true;
    }
}
