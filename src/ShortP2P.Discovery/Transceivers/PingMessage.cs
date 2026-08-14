using ShortP2P.Auth.Data;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Discovery.Transceivers;

/// <summary>
///     Распарсенный presence ping (frame 0x31) с метаданными отправителя.
/// </summary>
public sealed class PingMessage(
    CompressedNetworkId peerNetworkId,
    string nickname,
    int peerDataUdpPort,
    LinkTechnologyPreset advertisedLink,
    PresencePeerCapabilities advertisedCapabilities,
    ReadOnlyMemory<byte> rawPayload,
    TransportAddress remoteAddress)
{
    public CompressedNetworkId PeerNetworkId { get; } = peerNetworkId;
    public string Nickname { get; } = nickname;
    public int PeerDataUdpPort { get; } = peerDataUdpPort;
    public LinkTechnologyPreset AdvertisedLink { get; } = advertisedLink;
    public PresencePeerCapabilities AdvertisedCapabilities { get; } = advertisedCapabilities;
    public ReadOnlyMemory<byte> RawPayload { get; } = rawPayload;
    public TransportAddress RemoteAddress { get; } = remoteAddress;
}