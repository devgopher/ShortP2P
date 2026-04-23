using System.Buffers.Binary;
using System.Text;

namespace ShortP2P.Client.Routing;

/// <summary>
///     Кодек discovery/presence ping на порту <see cref="UdpPort" />: network id + nickname (UTF-8) + порт data-UDP.
///     Формат: [0]=frame, [1..16]=Guid, [17..18]=длина ника, [19..]=UTF-8 ник, uint16 BE dataUdpPort,
///     [+1]=<see cref="LinkTechnologyPreset" /> (опционально), [+2]=uint16 BE <see cref="PresencePeerCapabilities" /> (опционально, на будущее).
///     Совместимость: 17 байт только Guid; 19+nick — ник без порта; без байта скорости — <see cref="LinkTechnologyPreset.Unlimited" />;
///     без двух байт маски — считается только Messaging (<see cref="PresencePeerCapabilities.Chat" />) у отправителя legacy-клиента.
///     Полный перечень ролей узла — README ShortP2P.Discovery, раздел «Узел и возможности».
/// </summary>
public static class PresencePingCodec
{
    /// <summary>Локальный и удалённый UDP-порт только для discovery/presence ping.</summary>
    public const int UdpPort = 50101;

    public const byte FramePresencePing = 0x31;

    public const int MaxNicknameUtf8Bytes = 512;

    /// <summary>Если в пакете нет поля порта (старые клиенты).</summary>
    public const int DefaultDataUdpPort = 50100;

    public static byte[] Build(Guid networkId, string nickname, int dataUdpPort,
        LinkTechnologyPreset advertisedLink = LinkTechnologyPreset.Unlimited,
        PresencePeerCapabilities advertisedCapabilities = PresencePeerCapabilities.Chat)
    {
        nickname ??= string.Empty;
        if (dataUdpPort is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(dataUdpPort));

        var nickBytes = Encoding.UTF8.GetBytes(nickname.Trim());
        if (nickBytes.Length > MaxNicknameUtf8Bytes)
            nickBytes = nickBytes.AsSpan(0, MaxNicknameUtf8Bytes).ToArray();

        const int trailerAfterPort = 1 + 2; // LinkTechnology + capabilities BE
        var buf = new byte[1 + 16 + 2 + nickBytes.Length + 2 + trailerAfterPort];
        buf[0] = FramePresencePing;
        networkId.TryWriteBytes(buf.AsSpan(1, 16));
        BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(17, 2), (ushort)nickBytes.Length);
        nickBytes.CopyTo(buf.AsSpan(19));
        var portOff = 19 + nickBytes.Length;
        BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(portOff, 2), (ushort)dataUdpPort);
        buf[portOff + 2] = (byte)advertisedLink;
        var cap = (ushort)((ushort)advertisedCapabilities & (ushort)PresencePeerCapabilities.AllDefined);
        cap |= (ushort)PresencePeerCapabilities.Chat;
        BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(portOff + 3, 2), cap);
        return buf;
    }

    public static bool TryParse(ReadOnlySpan<byte> datagram, out Guid networkId, out string nickname,
        out int dataUdpPort, out LinkTechnologyPreset advertisedLink,
        out PresencePeerCapabilities advertisedCapabilities)
    {
        networkId = Guid.Empty;
        nickname = "";
        dataUdpPort = DefaultDataUdpPort;
        advertisedLink = LinkTechnologyPreset.Unlimited;
        advertisedCapabilities = PresencePeerCapabilities.Chat;

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

        var afterNick = 19 + nickLen;
        if (datagram.Length == afterNick + 2)
        {
            dataUdpPort = BinaryPrimitives.ReadUInt16BigEndian(datagram.Slice(afterNick, 2));
            return dataUdpPort is >= 1 and <= 65535;
        }

        if (datagram.Length < afterNick + 3)
            return false;

        dataUdpPort = BinaryPrimitives.ReadUInt16BigEndian(datagram.Slice(afterNick, 2));
        if (dataUdpPort is < 1 or > 65535)
            return false;
        var lt = (LinkTechnologyPreset)datagram[afterNick + 2];
        if (!Enum.IsDefined(lt))
            return false;
        advertisedLink = lt;

        if (datagram.Length == afterNick + 3)
            return true;

        if (datagram.Length < afterNick + 5)
            return false;

        var raw = BinaryPrimitives.ReadUInt16BigEndian(datagram.Slice(afterNick + 3, 2));
        advertisedCapabilities =
            (PresencePeerCapabilities)(raw & (ushort)PresencePeerCapabilities.AllDefined);
        advertisedCapabilities |= PresencePeerCapabilities.Chat;
        return true;
    }
}
