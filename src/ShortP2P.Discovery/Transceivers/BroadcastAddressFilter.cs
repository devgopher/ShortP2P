using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using ShortP2P.Transport;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Discovery.Transceivers;

/// <summary>
///     Распознаёт IPv4 broadcast-адреса (limited 255.255.255.255 и per-subnet «.x.255»),
///     чтобы запретить unicast-only транспиверам отправлять/принимать в широковещательном режиме.
/// </summary>
internal static class BroadcastAddressFilter
{
    public static bool IsLocalIpv4Broadcast(TransportAddress address)
    {
        if (address.Kind != TransportKind.Udp)
            return false;
        try
        {
            var ip = UdpTransportAddress.ToIPEndPoint(address).Address;
            return IsLocalIpv4Broadcast(ip);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsLocalIpv4Broadcast(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork)
            return false;
        if (address.Equals(IPAddress.Broadcast))
            return true;

        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            foreach (var ua in ni.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                var mask = ua.IPv4Mask;
                if (mask == null) continue;
                if (ComputeBroadcast(ua.Address, mask).Equals(address))
                    return true;
            }
        }

        return false;
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
