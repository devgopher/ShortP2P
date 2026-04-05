using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Discovery;

/// <summary>
///     Найденный рядом абонент и адрес для доставки на транспортном уровне (например UDP).
/// </summary>
public sealed class DiscoveredPeer
{
    public required PeerIdentity Identity { get; init; }

    /// <summary>Адрес источника beacon (обычно порт discovery).</summary>
    public required TransportAddress ReachableAt { get; init; }

    /// <summary>Адрес для UDP данных (IP с beacon, порт из beacon).</summary>
    public required TransportAddress DataReachableAt { get; init; }

    public required DateTimeOffset LastSeenUtc { get; init; }
}