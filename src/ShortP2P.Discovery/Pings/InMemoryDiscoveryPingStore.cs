using System.Collections.Concurrent;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Discovery.Pings;

public sealed class InMemoryDiscoveryPingStore : IDiscoveryPingStore
{
    private readonly ConcurrentDictionary<string, DiscoveryPingEntry> _entries = new(StringComparer.Ordinal);

    public void Write(PeerIdentity identity, TransportAddress address, DateTimeOffset pingedAtUtc)
    {
        var key = BuildKey(identity.NetworkId.Value, address);
        var entry = new DiscoveryPingEntry(identity, address, pingedAtUtc);
        _entries.AddOrUpdate(key, entry, (_, _) => entry);
    }

    public IReadOnlyList<DiscoveryPingEntry> GetSnapshot() =>
        _entries.Values
            .OrderByDescending(e => e.LastSeenUtc)
            .ToArray();

    private static string BuildKey(Guid networkId, TransportAddress address) =>
        $"{networkId:N}:{(int)address.Kind}:{Convert.ToBase64String(address.Data)}";
}
