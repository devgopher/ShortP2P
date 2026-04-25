using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Discovery.Pings;

public sealed record DiscoveryPingEntry(
    Guid NetworkId,
    TransportAddress Address,
    DateTimeOffset PingedAtUtc);
