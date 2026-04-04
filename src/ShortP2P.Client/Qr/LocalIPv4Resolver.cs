using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace ShortP2P.Client.Qr;

/// <summary>Picks a reasonable local IPv4 for QR display (best-effort; user may override).</summary>
public static class LocalIPv4Resolver
{
    public static string? TryGetPreferredUnicastIpv4()
    {
        var scored = new List<(string Ip, int Score)>();
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up)
                continue;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            foreach (var ua in ni.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork)
                    continue;
                if (IPAddress.IsLoopback(ua.Address))
                    continue;

                var ip = ua.Address.ToString();
                var score = Score(ua.Address);
                scored.Add((ip, score));
            }
        }

        return scored.OrderByDescending(x => x.Score).Select(x => x.Ip).FirstOrDefault();
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
