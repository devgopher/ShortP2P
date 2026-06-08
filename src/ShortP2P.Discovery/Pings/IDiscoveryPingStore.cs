using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Discovery.Pings;

public interface IDiscoveryPingStore
{
    void Write(PeerIdentity identity, TransportAddress address, DateTimeOffset pingedAtUtc);

    IReadOnlyList<DiscoveryPingEntry> GetSnapshot();
}