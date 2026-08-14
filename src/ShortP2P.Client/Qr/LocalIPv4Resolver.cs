using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace ShortP2P.Discovery;

/// <summary>Picks a reasonable local IPv4 for QR display (best-effort; user may override).</summary>
public static class LocalIPv4Resolver
{
    private static readonly string[] PublicIpLookupUrls =
    [
        "https://api.ipify.org",
        "https://ipv4.icanhazip.com",
        "https://ifconfig.me/ip"
    ];

    public static string? TryGetPreferredUnicastIpv4()
    {
        var scored = new List<(string Ip, int Score)>();
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up)
                continue;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            foreach (var ua in ni.GetIPProperties()
                         .UnicastAddresses
                         .Select(u => u.Address))
            {
                if (ua.AddressFamily != AddressFamily.InterNetwork)
                    continue;
                if (IPAddress.IsLoopback(ua))
                    continue;

                var ip = ua.ToString();
                var score = Score(ua);
                scored.Add((ip, score));
            }
        }

        return scored.OrderByDescending(x => x.Score).Select(x => x.Ip).FirstOrDefault();
    }

    /// <summary>Все поднятые unicast IPv4 (без loopback), от лучшего к худшему по эвристике <see cref="Score" />.</summary>
    public static List<string> GetAllUnicastIpv4Ordered()
    {
        var scored = new List<(string Ip, int Score)>();
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up)
                continue;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            foreach (var ua in ni.GetIPProperties().UnicastAddresses.Select(u => u.Address))
            {
                if (ua.AddressFamily != AddressFamily.InterNetwork)
                    continue;
                if (IPAddress.IsLoopback(ua))
                    continue;

                scored.Add((ua.ToString(), Score(ua)));
            }
        }

        return scored
            .OrderByDescending(x => x.Score)
            .Select(x => x.Ip)
            .Distinct()
            .ToList();
    }

    /// <summary>Все поднятые unicast IPv6 (без loopback), лексикографически.</summary>
    public static List<string> GetAllUnicastIpv6Ordered()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up)
                continue;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            foreach (var ua in ni.GetIPProperties().UnicastAddresses)
            {
                var addr = ua.Address;
                if (addr.AddressFamily != AddressFamily.InterNetworkV6)
                    continue;
                if (IPAddress.IsLoopback(addr))
                    continue;
                set.Add(addr.ToString());
            }
        }

        return set.OrderBy(x => x, StringComparer.Ordinal).ToList();
    }

    /// <summary>Best-effort public IPv4 via external HTTP echo services; null if unavailable.</summary>
    public static string? TryGetPublicIpv4(TimeSpan timeout)
    {
        using var http = new HttpClient { Timeout = timeout };
        foreach (var url in PublicIpLookupUrls)
            try
            {
                var text = http.GetStringAsync(url).GetAwaiter().GetResult();
                var candidate = text.Trim();
                if (!IPAddress.TryParse(candidate, out var ip))
                    continue;
                if (ip.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(ip))
                    continue;
                return ip.ToString();
            }
            catch
            {
                // try next endpoint
            }

        return null;
    }

    /// <summary>
    ///     Хосты для инвайта и QR: сначала публичный IPv4 (если удалось узнать), затем локальные unicast без дубликатов.
    /// </summary>
    public static List<string> GetInviteHostCandidatesOrdered(TimeSpan publicLookupTimeout)
    {
        var ordered = new List<string>();
        var publicIp = TryGetPublicIpv4(publicLookupTimeout);
        if (!string.IsNullOrWhiteSpace(publicIp))
            ordered.Add(publicIp.Trim());

        foreach (var ip in GetAllUnicastIpv4Ordered())
        {
            if (ordered.Contains(ip, StringComparer.OrdinalIgnoreCase))
                continue;
            ordered.Add(ip);
        }

        if (ordered.Count == 0)
            ordered.Add(LocalEndpointHelper.GetPreferredLanIPv4String());

        return ordered;
    }

    /// <summary>Список хостов для поля invite/ответа на инвайт (публичный предпочтительнее).</summary>
    public static string GetInviteHostsCommaSeparated(TimeSpan publicLookupTimeout)
    {
        return string.Join(", ", GetInviteHostCandidatesOrdered(publicLookupTimeout));
    }

    private static int Score(IPAddress a)
    {
        var b = a.GetAddressBytes();
        if (b[0] == 169 && b[1] == 254)
            return 1;
        if (b[0] == 192 && b[1] == 168)
            return 100;
        if (b[0] == 10)
            return 90;
        if (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            return 80;
        return 50;
    }
}