using System.Buffers.Binary;
using System.Text;
using ShortP2P.Auth.Data;

namespace ShortP2P.Discovery;

/// <summary>
///     Бинарный формат beacon: magic, версия, тип, 16 байт id, UTF-8 nickname.
/// </summary>
internal static class DiscoveryBeaconCodec
{
    private const byte Version = 1;

    private const byte MsgAnnounce = 1;

    private const int HeaderBytes = 4 + 1 + 1 + 2 + 16 + 2; // magic + ver + type + res + id + nicklen
    private static ReadOnlySpan<byte> Magic => "SP2D"u8;

    internal static byte[] EncodeAnnounce(PeerIdentity peer, int maxNicknameUtf8Bytes)
    {
        var nick = Encoding.UTF8.GetBytes(peer.Nickname);
        if (nick.Length > ushort.MaxValue) throw new ArgumentException("Nickname is too long.", nameof(peer));

        const int trailer = 2; // data UDP port BE
        var buf = new byte[HeaderBytes + nick.Length + trailer];
        Magic.CopyTo(buf.AsSpan(0, 4));
        buf[4] = Version;
        buf[5] = MsgAnnounce;
        buf[6] = 0;
        buf[7] = 0;
        if (!peer.NetworkId.TryWriteBytes(buf.AsSpan(8, 16)))
            throw new InvalidOperationException();
        BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(24, 2), (ushort)nick.Length);
        nick.CopyTo(buf.AsSpan(26));
        BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(26 + nick.Length, 2), (ushort)peer.DataUdpPort);
        return buf;
    }

    internal static bool TryParseAnnounce(ReadOnlySpan<byte> data, int maxNicknameUtf8Bytes, out PeerIdentity? peer)
    {
        peer = null;
        if (data.Length < HeaderBytes) return false;
        if (!data.StartsWith(Magic)) return false;
        if (data[4] != Version) return false;
        if (data[5] != MsgAnnounce) return false;

        var id = CompressedNetworkId.FromWireBytes(data.Slice(8, 16));
        var nickLen = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(24, 2));
        if (nickLen > maxNicknameUtf8Bytes) return false;
        var minLen = HeaderBytes + nickLen;
        if (data.Length < minLen) return false;

        ushort dataPort = 50100;
        if (data.Length >= minLen + 2)
        {
            dataPort = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(minLen, 2));
            if (dataPort == 0) return false;
            if (data.Length != minLen + 2) return false;
        }
        else if (data.Length != minLen)
        {
            return false;
        }

        var nick = Encoding.UTF8.GetString(data.Slice(26, nickLen));
        if (string.IsNullOrWhiteSpace(nick)) return false;

        try
        {
            peer = new PeerIdentity(nick, id, dataPort, maxNicknameUtf8Bytes);
        }
        catch (ArgumentException)
        {
            return false;
        }

        return true;
    }
}