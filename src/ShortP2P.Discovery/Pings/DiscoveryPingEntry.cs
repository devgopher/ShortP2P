using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Discovery.Pings;

public sealed record DiscoveryPingEntry(
    PeerIdentity Identity,
    TransportAddress Address,
    DateTimeOffset LastSeenUtc);
