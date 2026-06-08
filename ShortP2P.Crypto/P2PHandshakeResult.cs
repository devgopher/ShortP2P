namespace ShortP2P.Crypto;

/// <summary>
///     Result of the handshake initiation:
///     initiator gets both the packet to send and a ready-to-use session for encrypting packets.
/// </summary>
public sealed class P2PHandshakeResult
{
    internal P2PHandshakeResult(byte[] handshakePacket, P2PSession session)
    {
        HandshakePacket = handshakePacket;
        Session = session;
    }

    public byte[] HandshakePacket { get; }
    public P2PSession Session { get; }
}