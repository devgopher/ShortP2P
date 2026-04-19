using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Client.Routing;

/// <summary>
///     Узел, обнаруженный по discovery-пингу (порт <see cref="PresencePingCodec.UdpPort" /> или Bluetooth).
/// </summary>
/// <param name="PeerDataUdpPort">Порт пира для data/чата (из пинга или <see cref="PresencePingCodec.DefaultDataUdpPort" />).</param>
/// <param name="AdvertisedLinkTechnology">Пресет канала из пинга отправителя.</param>
/// <param name="AdvertisedCapabilities">Маска возможностей из пинга; у старых клиентов без поля — только <see cref="PresencePeerCapabilities.Chat" />.</param>
public sealed record DiscoveredLocalPeer(
    Guid NetworkId,
    string Nickname,
    TransportAddress SourceAddress,
    TransportKind TransportKind,
    DateTimeOffset LastSeenUtc,
    int PeerDataUdpPort,
    LinkTechnologyPreset AdvertisedLinkTechnology = LinkTechnologyPreset.Unlimited,
    PresencePeerCapabilities AdvertisedCapabilities = PresencePeerCapabilities.Chat);

public sealed class DiscoveryPingReceivedEventArgs(DiscoveredLocalPeer peer) : EventArgs
{
    public DiscoveredLocalPeer Peer { get; } = peer;
}
