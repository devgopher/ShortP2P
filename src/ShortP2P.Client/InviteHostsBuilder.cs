using ShortP2P.Client.Qr;
using ShortP2P.Client.Routing;

namespace ShortP2P.Client;

/// <summary>
///     Список endpoint-кандидатов для поля host в <see cref="Routing.ChatInviteCodec" />:
///     IPv4/IPv6 (UDP), MAC Bluetooth, short network id.
/// </summary>
public static class InviteHostsBuilder
{
    public static string BuildCommaSeparated(
        P2pRoutingSettings? settings = null,
        string? bluetoothAdapterMac = null,
        string? networkIdShort = null,
        TimeSpan? publicLookupTimeout = null)
    {
        var timeout = publicLookupTimeout ?? TimeSpan.FromSeconds(2);
        var candidates = GetCandidatesOrdered(settings, bluetoothAdapterMac, networkIdShort, timeout);
        return candidates.Count > 0
            ? string.Join(", ", candidates)
            : LocalIPv4Resolver.GetInviteHostsCommaSeparated(timeout);
    }

    public static IReadOnlyList<string> GetCandidatesOrdered(
        P2pRoutingSettings? settings = null,
        string? bluetoothAdapterMac = null,
        string? networkIdShort = null,
        TimeSpan? publicLookupTimeout = null)
    {
        var timeout = publicLookupTimeout ?? TimeSpan.FromSeconds(2);
        string? merged = null;

        if (settings is null || settings.EnableUdpTransport)
        {
            foreach (var ip in LocalIPv4Resolver.GetInviteHostCandidatesOrdered(timeout))
                merged = PeerHostList.MergeAppend(merged, ip);
            foreach (var ip in LocalIPv4Resolver.GetAllUnicastIpv6Ordered())
                merged = PeerHostList.MergeAppend(merged, ip);
        }

        if ((settings is null || settings.EnableBluetoothTransport) && !string.IsNullOrWhiteSpace(bluetoothAdapterMac))
            merged = PeerHostList.MergeAppend(merged, bluetoothAdapterMac);

        if (!string.IsNullOrWhiteSpace(networkIdShort))
            merged = PeerHostList.MergeAppend(merged, networkIdShort);

        return merged == null ? [] : PeerHostList.ParseEndpointCandidates(merged);
    }
}
