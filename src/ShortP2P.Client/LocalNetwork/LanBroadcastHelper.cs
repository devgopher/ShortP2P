using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace ShortP2P.Client.LocalNetwork;

/// <summary>
///     IPv4 broadcast-адреса по локальным интерфейсам + 255.255.255.255.
/// </summary>
internal static class LanBroadcastHelper
{
    public static IEnumerable<IPEndPoint> GetIpv4BroadcastEndpoints(int port)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var broadcast in EnumerateBroadcastAddresses())
        {
            var key = broadcast.ToString();
            if (!seen.Add(key)) continue;
            yield return new IPEndPoint(broadcast, port);
        }

        if (seen.Add("255.255.255.255"))
            yield return new IPEndPoint(IPAddress.Broadcast, port);
    }

    private static IEnumerable<IPAddress> EnumerateBroadcastAddresses()
    {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            foreach (var ua in ni.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                if (IPAddress.IsLoopback(ua.Address)) continue;
                var mask = ua.IPv4Mask;
                if (mask == null) continue;
                yield return ComputeBroadcast(ua.Address, mask);
            }
        }
    }

    private static IPAddress ComputeBroadcast(IPAddress address, IPAddress mask)
    {
        var a = address.GetAddressBytes();
        var m = mask.GetAddressBytes();
        if (a.Length != 4 || m.Length != 4)
            throw new ArgumentException("IPv4 only.");
        var b = new byte[4];
        for (var i = 0; i < 4; i++) b[i] = (byte)(a[i] | ~m[i]);
        return new IPAddress(b);
    }
}
