using System.Buffers.Binary;
using System.Text;
using ShortP2P.Auth.Data;
using ShortP2P.Discovery.RouteTables;

namespace ShortP2P.Discovery.Gossip;

/// <summary>
///     Запрос полной маршрутной таблицы у узла с PeerSearch; тот же UDP, что <see cref="GossipWireCodec.UdpPort" />.
///     Формат независим от сборки Client: совместимый бит capability — 0x0002 в presence-пинге.
/// </summary>
public static class RouteTableWireCodec
{
    private const byte FrameRequest = 0x42;
    private const byte FrameReply = 0x43;

    private const int RequestLength = 1 + 8 + CompressedNetworkId.WireLength;
    private const int ReplyHeaderLength = 1 + 8 + CompressedNetworkId.WireLength + 2 + 2;

    private const ushort FlagTruncated = 1;

    /// <summary>Максимальный размер полезной нагрузки ответа (без IP-фрагментации).</summary>
    private const int DefaultMaxReplyLength = 32000;

    public static byte[] BuildRequest(long nonce, CompressedNetworkId senderNetworkId)
    {
        var buf = new byte[RequestLength];
        buf[0] = FrameRequest;
        BinaryPrimitives.WriteInt64LittleEndian(buf.AsSpan(1, 8), nonce);
        if (!senderNetworkId.TryWriteBytes(buf.AsSpan(9, CompressedNetworkId.WireLength)))
            throw new InvalidOperationException("Failed to write sender network id.");
        return buf;
    }

    public static bool TryParseRequest(ReadOnlySpan<byte> datagram, out long nonce,
        out CompressedNetworkId senderNetworkId)
    {
        nonce = 0;
        senderNetworkId = CompressedNetworkId.Empty;
        if (datagram.Length < RequestLength || datagram[0] != FrameRequest)
            return false;
        nonce = BinaryPrimitives.ReadInt64LittleEndian(datagram.Slice(1, 8));
        senderNetworkId = CompressedNetworkId.FromWireBytes(datagram.Slice(9, CompressedNetworkId.WireLength));
        return true;
    }

    public static byte[] BuildReply(long nonce, CompressedNetworkId responderNetworkId, IReadOnlyList<Route> routes,
        int maxTotalLength = DefaultMaxReplyLength)
    {
        var buf = new List<byte>(Math.Min(maxTotalLength, 4096))
        {
            FrameReply
        };
        var nonceBytes = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(nonceBytes, nonce);
        buf.AddRange(nonceBytes);
        var responderBytes = new byte[CompressedNetworkId.WireLength];
        if (!responderNetworkId.TryWriteBytes(responderBytes))
            throw new InvalidOperationException("Failed to write responder network id.");
        buf.AddRange(responderBytes);
        var flagsPos = buf.Count;
        buf.Add(0);
        buf.Add(0);
        var countPos = buf.Count;
        buf.Add(0);
        buf.Add(0);

        ushort added = 0;
        var flags = (ushort)0;
        foreach (var route in routes)
        {
            var chunk = SerializeRoute(route);
            if (buf.Count + chunk.Length > maxTotalLength)
            {
                flags |= FlagTruncated;
                break;
            }

            buf.AddRange(chunk);
            added++;
        }

        Span<byte> be = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(be, flags);
        buf[flagsPos] = be[0];
        buf[flagsPos + 1] = be[1];
        BinaryPrimitives.WriteUInt16BigEndian(be, added);
        buf[countPos] = be[0];
        buf[countPos + 1] = be[1];
        return buf.ToArray();
    }

    public static bool TryParseReply(ReadOnlySpan<byte> datagram, out long nonce,
        out List<Route> routes)
    {
        nonce = 0;
        routes = [];
        if (datagram.Length < ReplyHeaderLength || datagram[0] != FrameReply)
            return false;
        nonce = BinaryPrimitives.ReadInt64LittleEndian(datagram.Slice(1, 8));

        var routeCount = BinaryPrimitives.ReadUInt16BigEndian(datagram.Slice(23, 2));
        var off = ReplyHeaderLength;
        for (var i = 0; i < routeCount; i++)
        {
            if (!TryReadRoute(datagram, ref off, out var route))
                return false;
            routes.Add(route);
        }

        return off == datagram.Length;
    }

    private static byte[] SerializeRoute(Route route)
    {
        var parts = new List<byte>();
        var routeIdBytes = Encoding.UTF8.GetBytes(route.RouteId ?? "");
        if (routeIdBytes.Length > ushort.MaxValue)
            routeIdBytes = routeIdBytes.AsSpan(0, ushort.MaxValue).ToArray();
        WriteUInt16Be(parts, (ushort)routeIdBytes.Length);
        parts.AddRange(routeIdBytes);

        var peers = route.PeerRoutes ?? [];
        if (peers.Count > ushort.MaxValue)
            throw new InvalidOperationException("Too many peer routes for wire format.");
        WriteUInt16Be(parts, (ushort)peers.Count);
        foreach (var p in peers)
        {
            var prId = Encoding.UTF8.GetBytes(p.RouteId ?? "");
            if (prId.Length > ushort.MaxValue)
                prId = prId.AsSpan(0, ushort.MaxValue).ToArray();
            WriteUInt16Be(parts, (ushort)prId.Length);
            parts.AddRange(prId);

            var idBytes = new byte[CompressedNetworkId.WireLength];
            if (!p.PeerIdentity.NetworkId.TryWriteBytes(idBytes))
                throw new InvalidOperationException("Failed to write peer network id.");
            parts.AddRange(idBytes);

            if (p.PeerIdentity.DataUdpPort is < 1 or > 65535)
                throw new ArgumentOutOfRangeException(nameof(p.PeerIdentity.DataUdpPort));

            WriteUInt16Be(parts, (ushort)p.PeerIdentity.DataUdpPort);

            var nick = Encoding.UTF8.GetBytes(p.PeerIdentity.Nickname ?? "?");
            if (nick.Length > ushort.MaxValue)
                nick = nick.AsSpan(0, ushort.MaxValue).ToArray();
            WriteUInt16Be(parts, (ushort)nick.Length);
            parts.AddRange(nick);

            var addr = Encoding.UTF8.GetBytes(p.PeerAddress ?? "");
            if (addr.Length > ushort.MaxValue)
                addr = addr.AsSpan(0, ushort.MaxValue).ToArray();
            WriteUInt16Be(parts, (ushort)addr.Length);
            parts.AddRange(addr);

            var ticks = p.LastSeen.Kind == DateTimeKind.Utc
                ? p.LastSeen.Ticks
                : p.LastSeen.ToUniversalTime().Ticks;
            WriteInt64Le(parts, ticks);
        }

        return parts.ToArray();
    }

    private static bool TryReadRoute(ReadOnlySpan<byte> datagram, ref int off, out Route route)
    {
        route = null!;
        if (off + 2 > datagram.Length)
            return false;
        var routeIdLen = BinaryPrimitives.ReadUInt16BigEndian(datagram.Slice(off, 2));
        off += 2;
        if (off + routeIdLen > datagram.Length)
            return false;
        var routeId = Utf8Span.GetString(datagram.Slice(off, routeIdLen));
        off += routeIdLen;
        if (off + 2 > datagram.Length)
            return false;
        var peerCount = BinaryPrimitives.ReadUInt16BigEndian(datagram.Slice(off, 2));
        off += 2;
        var peerList = new List<PeerIdentityAddress>();
        for (var j = 0; j < peerCount; j++)
        {
            if (off + 2 > datagram.Length)
                return false;
            var prLen = BinaryPrimitives.ReadUInt16BigEndian(datagram.Slice(off, 2));
            off += 2;
            if (off + prLen > datagram.Length)
                return false;
            var prRouteId = Utf8Span.GetString(datagram.Slice(off, prLen));
            off += prLen;
            if (off + CompressedNetworkId.WireLength + 2 + 2 > datagram.Length)
                return false;
            var cn = CompressedNetworkId.FromWireBytes(datagram.Slice(off, CompressedNetworkId.WireLength));
            off += CompressedNetworkId.WireLength;
            var port = BinaryPrimitives.ReadUInt16BigEndian(datagram.Slice(off, 2));
            off += 2;
            if (port is < 1 or > 65535)
                return false;
            var nickLen = BinaryPrimitives.ReadUInt16BigEndian(datagram.Slice(off, 2));
            off += 2;
            if (off + nickLen > datagram.Length)
                return false;
            var nick = Utf8Span.GetString(datagram.Slice(off, nickLen));
            off += nickLen;
            if (off + 2 > datagram.Length)
                return false;
            var addrLen = BinaryPrimitives.ReadUInt16BigEndian(datagram.Slice(off, 2));
            off += 2;
            if (off + addrLen + 8 > datagram.Length)
                return false;
            var addr = Utf8Span.GetString(datagram.Slice(off, addrLen));
            off += addrLen;
            var ticks = BinaryPrimitives.ReadInt64LittleEndian(datagram.Slice(off, 8));
            off += 8;
            var peerId = new PeerIdentity(
                string.IsNullOrWhiteSpace(nick) ? "?" : nick.Trim(),
                cn,
                port,
                GossipWireCodec.MaxNicknameUtf8Bytes);
            peerList.Add(new PeerIdentityAddress
            {
                RouteId = prRouteId,
                PeerIdentity = peerId,
                PeerAddress = addr,
                LastSeen = new DateTime(ticks, DateTimeKind.Utc)
            });
        }

        route = new Route
        {
            RouteId = routeId,
            PeerRoutes = peerList
        };
        return true;
    }

    private static void WriteUInt16Be(List<byte> buf, ushort v)
    {
        var b = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(b, v);
        buf.AddRange(b);
    }

    private static void WriteInt64Le(List<byte> buf, long v)
    {
        var b = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(b, v);
        buf.AddRange(b);
    }
}