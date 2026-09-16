namespace ShortP2P.MessengerServer.Contracts;

/// <summary>
/// Payload limits for <c>POST /api/v1/forward</c> peer-profile frames.
/// Mirrors <c>PeerProfileLimits</c> / <c>PeerProfileWireCodec</c> (Avatar ≤20 KB).
/// </summary>
public static class ForwardLimits
{
    public const int NetworkIdWireLength = 12;

    public const byte FrameRequest = 0x44;
    public const byte FrameReply = 0x45;

    public const int RequestLength = 1 + 8 + NetworkIdWireLength;

    public const int MaxAboutMeUtf8Bytes = 250 * 4;

    public const int MaxAvatarBytes = 20 * 1024;

    /// <summary>Max decoded 0x45 length: frame + nonce + id + aboutLen + about + avatarLen + avatar.</summary>
    public const int MaxReplyLength =
        1 + 8 + NetworkIdWireLength + 2 + MaxAboutMeUtf8Bytes + 2 + MaxAvatarBytes;

    /// <summary>Ephemeral inbox TTL before undelivered forwards are dropped.</summary>
    public static readonly TimeSpan ForwardTtl = TimeSpan.FromSeconds(60);
}
