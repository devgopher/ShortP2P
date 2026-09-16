namespace ShortP2P.MessengerServer.Contracts.Dtos;

/// <summary>Ephemeral peer-profile forward kinds (LAN wire 0x44 / 0x45).</summary>
public enum ForwardKind
{
    PeerProfileRequest = 0,
    PeerProfileReply = 1
}
