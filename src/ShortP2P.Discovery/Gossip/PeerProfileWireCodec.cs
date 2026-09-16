using System.Buffers.Binary;
using System.Text;
using ShortP2P.Auth.Data;

namespace ShortP2P.Discovery.Gossip;

/// <summary>
///     Запрос-ответ профиля абонента (AboutMe + Avatar) на UDP
///     <see cref="UdpPeerDiscoveryOptions.DefaultDiscoveryUdpPort" />.
///     Request <c>0x44</c>: nonce LE + senderNetworkId.
///     Reply <c>0x45</c>: nonce LE + responderNetworkId + aboutLen BE + AboutMe UTF-8 + avatarLen BE + Avatar.
/// </summary>
public static class PeerProfileWireCodec
{
    public const byte FrameRequest = 0x44;
    public const byte FrameReply = 0x45;

    public const int RequestLength = 1 + 8 + CompressedNetworkId.WireLength;

    /// <summary>До поля AboutMe: frame + nonce + id + aboutLen + avatarLen placeholders after about.</summary>
    public const int ReplyFixedPrefixLength = 1 + 8 + CompressedNetworkId.WireLength + 2;

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

    public static byte[] BuildReply(long nonce, CompressedNetworkId responderNetworkId, string aboutMe,
        byte[]? avatar)
    {
        aboutMe ??= "";
        if (aboutMe.Length > PeerProfileLimits.MaxAboutMeChars)
            aboutMe = aboutMe[..PeerProfileLimits.MaxAboutMeChars];

        var aboutBytes = Encoding.UTF8.GetBytes(aboutMe);
        if (aboutBytes.Length > PeerProfileLimits.MaxAboutMeUtf8Bytes)
            aboutBytes = aboutBytes.AsSpan(0, PeerProfileLimits.MaxAboutMeUtf8Bytes).ToArray();

        ReadOnlySpan<byte> avatarSpan = avatar ?? ReadOnlySpan<byte>.Empty;
        if (avatarSpan.Length > PeerProfileLimits.MaxAvatarBytes)
            avatarSpan = avatarSpan[..PeerProfileLimits.MaxAvatarBytes];

        // [0]=frame, [1..8]=nonce, [9..]=id, aboutLen u16 BE, about, avatarLen u16 BE, avatar
        var buf = new byte[ReplyFixedPrefixLength + aboutBytes.Length + 2 + avatarSpan.Length];
        buf[0] = FrameReply;
        BinaryPrimitives.WriteInt64LittleEndian(buf.AsSpan(1, 8), nonce);
        if (!responderNetworkId.TryWriteBytes(buf.AsSpan(9, CompressedNetworkId.WireLength)))
            throw new InvalidOperationException("Failed to write responder network id.");

        var aboutLenOff = 1 + 8 + CompressedNetworkId.WireLength;
        BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(aboutLenOff, 2), (ushort)aboutBytes.Length);
        aboutBytes.CopyTo(buf.AsSpan(aboutLenOff + 2));

        var avatarLenOff = aboutLenOff + 2 + aboutBytes.Length;
        BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(avatarLenOff, 2), (ushort)avatarSpan.Length);
        avatarSpan.CopyTo(buf.AsSpan(avatarLenOff + 2));
        return buf;
    }

    public static bool TryParseReply(ReadOnlySpan<byte> datagram, out long nonce,
        out CompressedNetworkId responderNetworkId, out string aboutMe, out byte[]? avatar)
    {
        nonce = 0;
        responderNetworkId = CompressedNetworkId.Empty;
        aboutMe = "";
        avatar = null;

        if (datagram.Length < ReplyFixedPrefixLength || datagram[0] != FrameReply)
            return false;

        nonce = BinaryPrimitives.ReadInt64LittleEndian(datagram.Slice(1, 8));
        responderNetworkId = CompressedNetworkId.FromWireBytes(datagram.Slice(9, CompressedNetworkId.WireLength));

        var aboutLenOff = 1 + 8 + CompressedNetworkId.WireLength;
        var aboutLen = BinaryPrimitives.ReadUInt16BigEndian(datagram.Slice(aboutLenOff, 2));
        if (aboutLen > PeerProfileLimits.MaxAboutMeUtf8Bytes)
            return false;
        if (datagram.Length < aboutLenOff + 2 + aboutLen + 2)
            return false;

        try
        {
            aboutMe = Utf8Span.GetString(datagram.Slice(aboutLenOff + 2, aboutLen));
        }
        catch
        {
            return false;
        }

        if (aboutMe.Length > PeerProfileLimits.MaxAboutMeChars)
            aboutMe = aboutMe[..PeerProfileLimits.MaxAboutMeChars];

        var avatarLenOff = aboutLenOff + 2 + aboutLen;
        var avatarLen = BinaryPrimitives.ReadUInt16BigEndian(datagram.Slice(avatarLenOff, 2));
        if (avatarLen > PeerProfileLimits.MaxAvatarBytes)
            return false;
        if (datagram.Length < avatarLenOff + 2 + avatarLen)
            return false;

        if (avatarLen == 0)
            avatar = null;
        else
            avatar = datagram.Slice(avatarLenOff + 2, avatarLen).ToArray();

        return true;
    }
}
