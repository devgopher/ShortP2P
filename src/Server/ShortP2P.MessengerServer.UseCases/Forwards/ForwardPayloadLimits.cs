namespace ShortP2P.MessengerServer.UseCases.Forwards;

/// <summary>
/// Payload limits for peer-profile forwards (mirrors PeerProfileLimits / PeerProfileWireCodec).
/// </summary>
internal static class ForwardPayloadLimits
{
    public const int NetworkIdWireLength = 12;

    public const byte FrameRequest = 0x44;
    public const byte FrameReply = 0x45;

    public const int RequestLength = 1 + 8 + NetworkIdWireLength;

    public const int MaxAboutMeUtf8Bytes = 250 * 4;

    public const int MaxAvatarBytes = 20 * 1024;

    public const int MaxReplyLength =
        1 + 8 + NetworkIdWireLength + 2 + MaxAboutMeUtf8Bytes + 2 + MaxAvatarBytes;

    public static readonly TimeSpan ForwardTtl = TimeSpan.FromSeconds(60);
}
