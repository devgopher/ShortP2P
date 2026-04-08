using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Client.Routing;

/// <summary>
///     Узел, обнаруженный по discovery-пингу (порт <see cref="PresencePingCodec.UdpPort" /> или Bluetooth).
/// </summary>
public sealed record DiscoveredLocalPeer(
    Guid NetworkId,
    string Nickname,
    TransportAddress SourceAddress,
    TransportKind TransportKind,
    DateTimeOffset LastSeenUtc);

public sealed class DiscoveryPingReceivedEventArgs(DiscoveredLocalPeer peer) : EventArgs
{
    public DiscoveredLocalPeer Peer { get; } = peer;
}
