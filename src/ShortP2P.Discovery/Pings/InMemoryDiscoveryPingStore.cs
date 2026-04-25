using System.Collections.Concurrent;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Discovery.Pings;

public sealed class InMemoryDiscoveryPingStore : IDiscoveryPingStore
{
    private readonly ConcurrentDictionary<string, DiscoveryPingEntry> _entries = new(StringComparer.Ordinal);

    public void Write(Guid networkId, TransportAddress address, DateTimeOffset pingedAtUtc)
    {
        var key = BuildKey(networkId, address);
        var entry = new DiscoveryPingEntry(networkId, address, pingedAtUtc);
        _entries.AddOrUpdate(key, entry, (_, _) => entry);
    }

    public IReadOnlyList<DiscoveryPingEntry> GetSnapshot() =>
        _entries.Values
            .OrderByDescending(e => e.PingedAtUtc)
            .ToArray();

    private static string BuildKey(Guid networkId, TransportAddress address) =>
        $"{networkId:N}:{(int)address.Kind}:{Convert.ToBase64String(address.Data)}";
}
