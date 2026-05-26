using System.Buffers.Binary;
using System.Text;
using ShortP2P.Auth.Data;
using ShortP2P.Transport;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Client.Routing;

/// <summary>Служебные кадры LAN: поиск пира (до 3 рёбер), ответ, ретрансляция UDP.</summary>
public static class LanRoutingCodec
{
    public const byte FrameFind = 0x10;
    public const byte FrameFound = 0x11;
    public const byte FrameRelay = 0x22;

    private static ReadOnlySpan<byte> Magic => "SP2F"u8;
    private const byte WireVersion = 1;
    private const byte MsgFind = 1;
    private const byte MsgFound = 2;

    public static byte[] BuildFind(Guid searchId, CompressedNetworkId targetNetworkId, string targetNickname, byte ttl,
        IReadOnlyList<CompressedNetworkId> visited, IReadOnlyList<TransportAddress> pathDataHops)
    {
        var nick = Encoding.UTF8.GetBytes(targetNickname.Trim());
        if (nick.Length > ushort.MaxValue)
            throw new ArgumentException("Nickname too long.", nameof(targetNickname));
        if (ttl is < 1 or > 3)
            throw new ArgumentOutOfRangeException(nameof(ttl));
        if (visited.Count > 8)
            throw new ArgumentException("Too many visited nodes.", nameof(visited));
        if (pathDataHops.Count > 3)
            throw new ArgumentOutOfRangeException(nameof(pathDataHops));
        foreach (var a in pathDataHops)
        {
            if (a.Kind != TransportKind.Udp)
                throw new ArgumentException("Path must be UDP.", nameof(pathDataHops));
        }

        var pathBytes = 0;
        foreach (var a in pathDataHops)
            pathBytes += 2 + a.Data.Length;

        var bodyLen = 4 + 1 + 1 + 16 + CompressedNetworkId.WireLength + 2 + nick.Length + 1 + 1 +
                      visited.Count * CompressedNetworkId.WireLength + 1 + pathBytes;
        var buf = new byte[1 + bodyLen];
        buf[0] = FrameFind;
        var o = 1;
        Magic.CopyTo(buf.AsSpan(o, 4));
        o += 4;
        buf[o++] = WireVersion;
        buf[o++] = MsgFind;
        searchId.TryWriteBytes(buf.AsSpan(o, 16));
        o += 16;
        if (!targetNetworkId.TryWriteBytes(buf.AsSpan(o, CompressedNetworkId.WireLength)))
            throw new InvalidOperationException("Failed to write target network id.");
        o += CompressedNetworkId.WireLength;
        BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(o, 2), (ushort)nick.Length);
        o += 2;
        nick.CopyTo(buf.AsSpan(o, nick.Length));
        o += nick.Length;
        buf[o++] = ttl;
        buf[o++] = (byte)visited.Count;
        foreach (var id in visited)
        {
            if (!id.TryWriteBytes(buf.AsSpan(o, CompressedNetworkId.WireLength)))
                throw new InvalidOperationException("Failed to write visited network id.");
            o += CompressedNetworkId.WireLength;
        }

        buf[o++] = (byte)pathDataHops.Count;
        foreach (var a in pathDataHops)
        {
            BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(o, 2), (ushort)a.Data.Length);
            o += 2;
            a.Data.CopyTo(buf.AsSpan(o, a.Data.Length));
            o += a.Data.Length;
        }

        return buf;
    }

    public static bool TryParseFind(ReadOnlySpan<byte> datagram, out Guid searchId, out CompressedNetworkId targetNetworkId,
        out string targetNickname, out byte ttl, out List<CompressedNetworkId> visited, out List<TransportAddress> pathDataHops)
    {
        searchId = default;
        targetNetworkId = CompressedNetworkId.Empty;
        targetNickname = "";
        ttl = 0;
        visited = [];
        pathDataHops = [];
        if (datagram.Length == 0 || datagram[0] != FrameFind)
            return false;
        var d = datagram.Slice(1);
        if (d.Length < 4 + 1 + 1 + 16 + CompressedNetworkId.WireLength + 2 + 1 + 1)
            return false;
        if (!d.StartsWith(Magic)) return false;
        if (d[4] != WireVersion || d[5] != MsgFind) return false;
        var o = 6;
        searchId = new Guid(d.Slice(o, 16));
        o += 16;
        targetNetworkId = CompressedNetworkId.FromWireBytes(d.Slice(o, CompressedNetworkId.WireLength));
        o += CompressedNetworkId.WireLength;
        var nickLen = BinaryPrimitives.ReadUInt16BigEndian(d.Slice(o, 2));
        o += 2;
        if (nickLen > 4096 || d.Length < o + nickLen + 2) return false;
        targetNickname = Encoding.UTF8.GetString(d.Slice(o, nickLen));
        o += nickLen;
        ttl = d[o++];
        var vCount = d[o++];
        if (vCount > 8 || d.Length < o + vCount * CompressedNetworkId.WireLength + 1) return false;
        for (var i = 0; i < vCount; i++)
        {
            visited.Add(CompressedNetworkId.FromWireBytes(d.Slice(o, CompressedNetworkId.WireLength)));
            o += CompressedNetworkId.WireLength;
        }

        var pCount = d[o++];
        if (pCount > 3) return false;
        for (var i = 0; i < pCount; i++)
        {
            if (d.Length < o + 2) return false;
            var alen = BinaryPrimitives.ReadUInt16BigEndian(d.Slice(o, 2));
            o += 2;
            if (d.Length < o + alen) return false;
            pathDataHops.Add(new TransportAddress(TransportKind.Udp, d.Slice(o, alen).ToArray()));
            o += alen;
        }

        return d.Length == o;
    }

    /// <param name="firstRelayHop">Первый UDP-получатель от инициатора; null — прямой адрес peerHost/peerPort.</param>
    /// <param name="relayStripPath">Адреса для вложенного RELAY после первого хопа (длина ≤ 3).</param>
    public static byte[] BuildFound(Guid searchId, CompressedNetworkId targetNetworkId, string nickname, string rsaPublicKeyJson,
        string peerHost, int peerPort, TransportAddress? firstRelayHop, IReadOnlyList<TransportAddress> relayStripPath)
    {
        var nick = Encoding.UTF8.GetBytes(nickname.Trim());
        var pub = Encoding.UTF8.GetBytes(rsaPublicKeyJson);
        var host = Encoding.UTF8.GetBytes(peerHost.Trim());
        if (nick.Length > ushort.MaxValue || host.Length > ushort.MaxValue || pub.Length > 0x00FF_FFFF)
            throw new ArgumentException("Payload too large.");
        if (peerPort is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(peerPort));
        if (relayStripPath.Count > 3)
            throw new ArgumentOutOfRangeException(nameof(relayStripPath));
        if (firstRelayHop != null && firstRelayHop.Kind != TransportKind.Udp)
            throw new ArgumentException("Only UDP relay addresses supported.");
        foreach (var a in relayStripPath)
        {
            if (a.Kind != TransportKind.Udp)
                throw new ArgumentException("Only UDP relay addresses supported.");
        }

        var relayBytes = 0;
        if (firstRelayHop != null)
            relayBytes += 2 + firstRelayHop.Data.Length;
        foreach (var a in relayStripPath)
            relayBytes += 2 + a.Data.Length;

        var bodyLen = 4 + 1 + 1 + 16 + CompressedNetworkId.WireLength + 2 + nick.Length + 4 + pub.Length + 2 + host.Length + 2 + 1 + 1 + relayBytes;
        var buf = new byte[1 + bodyLen];
        buf[0] = FrameFound;
        var o = 1;
        Magic.CopyTo(buf.AsSpan(o, 4));
        o += 4;
        buf[o++] = WireVersion;
        buf[o++] = MsgFound;
        searchId.TryWriteBytes(buf.AsSpan(o, 16));
        o += 16;
        if (!targetNetworkId.TryWriteBytes(buf.AsSpan(o, CompressedNetworkId.WireLength)))
            throw new InvalidOperationException("Failed to write target network id.");
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
        BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(o, 2), (ushort)peerPort);
        o += 2;
        buf[o++] = (byte)(firstRelayHop != null ? 1 : 0);
        if (firstRelayHop != null)
        {
            BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(o, 2), (ushort)firstRelayHop.Data.Length);
            o += 2;
            firstRelayHop.Data.CopyTo(buf.AsSpan(o, firstRelayHop.Data.Length));
            o += firstRelayHop.Data.Length;
        }

        buf[o++] = (byte)relayStripPath.Count;
        foreach (var a in relayStripPath)
        {
            BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(o, 2), (ushort)a.Data.Length);
            o += 2;
            a.Data.CopyTo(buf.AsSpan(o, a.Data.Length));
            o += a.Data.Length;
        }

        return buf;
    }

    public static bool TryParseFound(ReadOnlySpan<byte> datagram, out Guid searchId, out CompressedNetworkId targetNetworkId,
        out string nickname, out string rsaPublicKeyJson, out string peerHost, out int peerPort,
        out TransportAddress? firstRelayHop, out List<TransportAddress> relayStripPath)
    {
        searchId = default;
        targetNetworkId = CompressedNetworkId.Empty;
        nickname = "";
        rsaPublicKeyJson = "";
        peerHost = "";
        peerPort = 0;
        firstRelayHop = null;
        relayStripPath = [];
        if (datagram.Length == 0 || datagram[0] != FrameFound)
            return false;
        var d = datagram.Slice(1);
        if (d.Length < 4 + 1 + 1 + 16 + CompressedNetworkId.WireLength + 2 + 4 + 2 + 2 + 1 + 1)
            return false;
        if (!d.StartsWith(Magic)) return false;
        if (d[4] != WireVersion || d[5] != MsgFound) return false;
        var o = 6;
        searchId = new Guid(d.Slice(o, 16));
        o += 16;
        targetNetworkId = CompressedNetworkId.FromWireBytes(d.Slice(o, CompressedNetworkId.WireLength));
        o += CompressedNetworkId.WireLength;
        var nickLen = BinaryPrimitives.ReadUInt16BigEndian(d.Slice(o, 2));
        o += 2;
        if (d.Length < o + nickLen) return false;
        nickname = Encoding.UTF8.GetString(d.Slice(o, nickLen));
        o += nickLen;
        var pubLen = BinaryPrimitives.ReadUInt32BigEndian(d.Slice(o, 4));
        o += 4;
        if (pubLen > 0x00FF_FFFF || d.Length < o + pubLen) return false;
        rsaPublicKeyJson = Encoding.UTF8.GetString(d.Slice(o, (int)pubLen));
        o += (int)pubLen;
        var hostLen = BinaryPrimitives.ReadUInt16BigEndian(d.Slice(o, 2));
        o += 2;
        if (d.Length < o + hostLen + 2) return false;
        peerHost = Encoding.UTF8.GetString(d.Slice(o, hostLen));
        o += hostLen;
        peerPort = BinaryPrimitives.ReadUInt16BigEndian(d.Slice(o, 2));
        o += 2;
        var hasFirst = d[o++];
        if (hasFirst > 1) return false;
        if (hasFirst == 1)
        {
            if (d.Length < o + 2) return false;
            var alen = BinaryPrimitives.ReadUInt16BigEndian(d.Slice(o, 2));
            o += 2;
            if (d.Length < o + alen) return false;
            firstRelayHop = new TransportAddress(TransportKind.Udp, d.Slice(o, alen).ToArray());
            o += alen;
        }

        if (d.Length < o + 1) return false;
        var stripCount = d[o++];
        if (stripCount > 3) return false;
        for (var i = 0; i < stripCount; i++)
        {
            if (d.Length < o + 2) return false;
            var alen = BinaryPrimitives.ReadUInt16BigEndian(d.Slice(o, 2));
            o += 2;
            if (d.Length < o + alen) return false;
            relayStripPath.Add(new TransportAddress(TransportKind.Udp, d.Slice(o, alen).ToArray()));
            o += alen;
        }

        return d.Length == o;
    }

    /// <summary>Строит кадр ретрансляции: hopCount адресов, затем полезная нагрузка (например 0x02+шифртекст).</summary>
    public static byte[] BuildRelay(IReadOnlyList<TransportAddress> remainingHops, ReadOnlySpan<byte> innerPayload)
    {
        if (remainingHops.Count > 3)
            throw new ArgumentOutOfRangeException(nameof(remainingHops));
        var hopBytes = 0;
        foreach (var a in remainingHops)
        {
            if (a.Kind != TransportKind.Udp)
                throw new ArgumentException("Only UDP hops.");
            hopBytes += 2 + a.Data.Length;
        }

        var buf = new byte[1 + 1 + hopBytes + innerPayload.Length];
        buf[0] = FrameRelay;
        buf[1] = (byte)remainingHops.Count;
        var o = 2;
        foreach (var a in remainingHops)
        {
            BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(o, 2), (ushort)a.Data.Length);
            o += 2;
            a.Data.CopyTo(buf.AsSpan(o, a.Data.Length));
            o += a.Data.Length;
        }

        innerPayload.CopyTo(buf.AsSpan(o));
        return buf;
    }

    public static bool TryParseRelay(ReadOnlySpan<byte> datagram, out byte hopCount, out ReadOnlySpan<byte> innerPayload)
    {
        hopCount = 0;
        innerPayload = ReadOnlySpan<byte>.Empty;
        if (datagram.Length < 3 || datagram[0] != FrameRelay)
            return false;
        hopCount = datagram[1];
        if (hopCount > 3) return false;
        var o = 2;
        for (var i = 0; i < hopCount; i++)
        {
            if (datagram.Length < o + 2) return false;
            var alen = BinaryPrimitives.ReadUInt16BigEndian(datagram.Slice(o, 2));
            o += 2;
            if (datagram.Length < o + alen) return false;
            o += alen;
        }

        innerPayload = datagram.Slice(o);
        return true;
    }

    /// <summary>Снимает первый хоп и возвращает пакет для отправки на nextAddr.</summary>
    public static bool TryStripRelayHop(ReadOnlySpan<byte> datagram, out TransportAddress? nextHop,
        out byte[]? forwardedPacket)
    {
        nextHop = null;
        forwardedPacket = null;
        if (datagram.Length < 4 || datagram[0] != FrameRelay)
            return false;
        var hopCount = datagram[1];
        if (hopCount == 0) return false;

        var o = 2;
        if (datagram.Length < o + 2) return false;
        var firstLen = BinaryPrimitives.ReadUInt16BigEndian(datagram.Slice(o, 2));
        o += 2;
        if (datagram.Length < o + firstLen) return false;
        nextHop = new TransportAddress(TransportKind.Udp, datagram.Slice(o, firstLen).ToArray());
        o += firstLen;

        var tail = datagram.Slice(o);
        forwardedPacket = new byte[1 + 1 + tail.Length];
        forwardedPacket[0] = FrameRelay;
        forwardedPacket[1] = (byte)(hopCount - 1);
        tail.CopyTo(forwardedPacket.AsSpan(2));
        return true;
    }

    public static bool RelayDeliversLocally(ReadOnlySpan<byte> datagram) =>
        TryParseRelay(datagram, out var hc, out _) && hc == 0;
}
