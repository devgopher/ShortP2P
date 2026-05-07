using System.Buffers.Binary;
using System.Text;

namespace ShortP2P.Discovery.Gossip;

/// <summary>
///     Запрос-ответ по UDP на <see cref="UdpPeerDiscoveryOptions.DefaultDiscoveryUdpPort" /> (как <see cref="UdpPeerDiscoveryOptions.DiscoveryPort" />).
/// </summary>
public static class GossipWireCodec
{
    /// <summary>Совпадает с <see cref="UdpPeerDiscoveryOptions.DiscoveryPort" /> по умолчанию.</summary>
    public const int UdpPort = UdpPeerDiscoveryOptions.DefaultDiscoveryUdpPort;

    public const byte FrameProbe = 0x40;
    public const byte FrameAck = 0x41;

    public const int MaxNicknameUtf8Bytes = 512;

    public const int ProbeLength = 1 + 8 + 16 + 16;
    public const int AckHeaderLength = 1 + 8 + 16 + 2 + 2;

    public static byte[] BuildProbe(long nonce, Guid senderNetworkId, Guid targetNetworkId)
    {
        var buf = new byte[ProbeLength];
        buf[0] = FrameProbe;
        BinaryPrimitives.WriteInt64LittleEndian(buf.AsSpan(1, 8), nonce);
        senderNetworkId.TryWriteBytes(buf.AsSpan(9, 16));
        targetNetworkId.TryWriteBytes(buf.AsSpan(25, 16));
        return buf;
    }

    public static bool TryParseProbe(ReadOnlySpan<byte> datagram, out long nonce, out Guid senderNetworkId,
        out Guid targetNetworkId)
    {
        nonce = 0;
        senderNetworkId = Guid.Empty;
        targetNetworkId = Guid.Empty;
        if (datagram.Length < ProbeLength || datagram[0] != FrameProbe)
            return false;
        nonce = BinaryPrimitives.ReadInt64LittleEndian(datagram.Slice(1, 8));
        senderNetworkId = new Guid(datagram.Slice(9, 16));
        targetNetworkId = new Guid(datagram.Slice(25, 16));
        return true;
    }

    public static byte[] BuildAck(long nonce, Guid responderNetworkId, int dataUdpPort, string nickname)
    {
        nickname ??= "?";
        var trimmed = nickname.Trim();
        if (trimmed.Length == 0)
            trimmed = "?";
        var nickBytes = Encoding.UTF8.GetBytes(trimmed);
        if (nickBytes.Length > MaxNicknameUtf8Bytes)
            nickBytes = nickBytes.AsSpan(0, MaxNicknameUtf8Bytes).ToArray();
        if (dataUdpPort is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(dataUdpPort));

        var buf = new byte[AckHeaderLength + nickBytes.Length];
        buf[0] = FrameAck;
        BinaryPrimitives.WriteInt64LittleEndian(buf.AsSpan(1, 8), nonce);
        responderNetworkId.TryWriteBytes(buf.AsSpan(9, 16));
        BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(25, 2), (ushort)dataUdpPort);
        BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(27, 2), (ushort)nickBytes.Length);
        nickBytes.CopyTo(buf.AsSpan(29));
        return buf;
    }

    public static bool TryParseAck(ReadOnlySpan<byte> datagram, out long nonce, out Guid responderNetworkId,
        out int dataUdpPort, out string nickname)
    {
        nonce = 0;
        responderNetworkId = Guid.Empty;
        dataUdpPort = 17500;
        nickname = "";
        if (datagram.Length < AckHeaderLength || datagram[0] != FrameAck)
            return false;
        nonce = BinaryPrimitives.ReadInt64LittleEndian(datagram.Slice(1, 8));
        responderNetworkId = new Guid(datagram.Slice(9, 16));
        dataUdpPort = BinaryPrimitives.ReadUInt16BigEndian(datagram.Slice(25, 2));
        if (dataUdpPort is < 1 or > 65535)
            return false;
        var nickLen = BinaryPrimitives.ReadUInt16BigEndian(datagram.Slice(27, 2));
        if (nickLen > MaxNicknameUtf8Bytes || datagram.Length < AckHeaderLength + nickLen)
            return false;
        try
        {
            nickname = Encoding.UTF8.GetString(datagram.Slice(29, nickLen));
        }
        catch
        {
            return false;
        }

        return true;
    }
}
