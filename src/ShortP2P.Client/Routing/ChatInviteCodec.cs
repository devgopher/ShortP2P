using System.Buffers.Binary;
using System.Text;
using ShortP2P.Auth.Data;
using ShortP2P.Discovery;

namespace ShortP2P.Client.Routing;

/// <summary>Неформализованное приглашение в чат: пир узнаёт о контакте при открытии чата собеседником.</summary>
public static class ChatInviteCodec
{
    public const byte FrameChatInvite = 0x30;

    /// <summary>
    ///     UDP-порт только для приглашений в чат; data/handshake/сообщения —
    ///     <see cref="PresencePingCodec.DefaultDataUdpPort" />.
    /// </summary>
    public const int InviteUdpPort = 17502;

    private const byte WireVersion = 1;

    private static ReadOnlySpan<byte> Magic => "SP2I"u8;

    /// <param name="dataPort">Порт в пакете для ответного invite (обычно <see cref="InviteUdpPort" />), не порт data-чата.</param>
    public static byte[] Build(string nickname, CompressedNetworkId networkId, string rsaPublicKeyJson,
        string dataHost, int dataPort)
    {
        var nick = Encoding.UTF8.GetBytes(nickname.Trim());
        var pub = Encoding.UTF8.GetBytes(rsaPublicKeyJson);
        var host = Encoding.UTF8.GetBytes(dataHost.Trim());
        if (nick.Length > ushort.MaxValue || host.Length > ushort.MaxValue || pub.Length > 0x00FF_FFFF)
            throw new ArgumentException("Field too long.");
        if (dataPort is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(dataPort));

        var bodyLen = 4 + 1 + CompressedNetworkId.WireLength + 2 + nick.Length + 4 + pub.Length + 2 + host.Length + 2;
        var buf = new byte[1 + bodyLen];
        buf[0] = FrameChatInvite;
        var o = 1;
        Magic.CopyTo(buf.AsSpan(o, 4));
        o += 4;
        buf[o++] = WireVersion;
        if (!networkId.TryWriteBytes(buf.AsSpan(o, CompressedNetworkId.WireLength)))
            throw new InvalidOperationException("Failed to write network id.");
        o += CompressedNetworkId.WireLength;
        BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(o, 2), (ushort)nick.Length);
        o += 2;
        nick.CopyTo(buf.AsSpan(o, nick.Length));
        o += nick.Length;
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(o, 4), (uint)pub.Length);
        o += 4;
        pub.CopyTo(buf.AsSpan(o, pub.Length));
        o += pub.Length;
        BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(o, 2), (ushort)host.Length);
        o += 2;
        host.CopyTo(buf.AsSpan(o, host.Length));
        o += host.Length;
        BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(o, 2), (ushort)dataPort);
        return buf;
    }

    public static bool TryParse(ReadOnlySpan<byte> datagram, out CompressedNetworkId initiatorNetworkId,
        out string nickname,
        out string rsaPublicKeyJson, out string dataHost, out int dataPort)
    {
        initiatorNetworkId = CompressedNetworkId.Empty;
        nickname = "";
        rsaPublicKeyJson = "";
        dataHost = "";
        dataPort = 0;
        if (datagram.Length < 2 || datagram[0] != FrameChatInvite)
            return false;
        var d = datagram.Slice(1);
        if (d.Length < 4 + 1 + CompressedNetworkId.WireLength + 2 + 4 + 2 + 2)
            return false;
        if (!d.StartsWith(Magic) || d[4] != WireVersion)
            return false;
        var o = 5;
        initiatorNetworkId = CompressedNetworkId.FromWireBytes(d.Slice(o, CompressedNetworkId.WireLength));
        o += CompressedNetworkId.WireLength;
        var nickLen = BinaryPrimitives.ReadUInt16BigEndian(d.Slice(o, 2));
        o += 2;
        if (d.Length < o + nickLen + 4) return false;
        nickname = Utf8Span.GetString(d.Slice(o, nickLen));
        o += nickLen;
        var pubLen = BinaryPrimitives.ReadUInt32BigEndian(d.Slice(o, 4));
        o += 4;
        if (pubLen > 0x00FF_FFFF || d.Length < o + pubLen + 2) return false;
        rsaPublicKeyJson = Utf8Span.GetString(d.Slice(o, (int)pubLen));
        o += (int)pubLen;
        var hostLen = BinaryPrimitives.ReadUInt16BigEndian(d.Slice(o, 2));
        o += 2;
        if (d.Length < o + hostLen + 2) return false;
        dataHost = Utf8Span.GetString(d.Slice(o, hostLen));
        o += hostLen;
        dataPort = BinaryPrimitives.ReadUInt16BigEndian(d.Slice(o, 2));
        o += 2;
        return d.Length == o && dataPort > 0;
    }
}