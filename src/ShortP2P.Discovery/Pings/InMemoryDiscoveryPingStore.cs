using System.Collections.Concurrent;
using ShortP2P.Auth.Data;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Discovery.Pings;

public sealed class InMemoryDiscoveryPingStore : IDiscoveryPingStore
{
    private readonly ConcurrentDictionary<string, DiscoveryPingEntry> _entries = new(StringComparer.Ordinal);

    public void Write(PeerIdentity identity, TransportAddress address, DateTimeOffset pingedAtUtc)
    {
        var key = BuildKey(identity.NetworkId, address);
        var entry = new DiscoveryPingEntry(identity, address, pingedAtUtc);
        _entries.AddOrUpdate(key, entry, (_, _) => entry);
    }

    public IReadOnlyList<DiscoveryPingEntry> GetSnapshot()
    {
        return _entries.Values
            .OrderByDescending(e => e.LastSeenUtc)
            .ToArray();
    }

    private static string BuildKey(CompressedNetworkId networkId, TransportAddress address)
    {
        return $"{networkId.ToShortString()}:{(int)address.Kind}:{Convert.ToBase64String(address.Data)}";
    }
}