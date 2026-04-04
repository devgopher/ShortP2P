using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Discovery;

/// <summary>
///     Найденный рядом абонент и адрес для доставки на транспортном уровне (например UDP).
/// </summary>
public sealed class DiscoveredPeer
{
    public required PeerIdentity Identity { get; init; }

    public required TransportAddress ReachableAt { get; init; }

    public required DateTimeOffset LastSeenUtc { get; init; }
}