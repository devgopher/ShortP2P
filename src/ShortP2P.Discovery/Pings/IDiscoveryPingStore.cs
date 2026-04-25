namespace ShortP2P.Discovery.Pings;

public interface IDiscoveryPingStore
{
    void Write(Guid networkId, ShortP2P.Transport.Abstractions.TransportAddress address, DateTimeOffset pingedAtUtc);

    IReadOnlyList<DiscoveryPingEntry> GetSnapshot();
}
