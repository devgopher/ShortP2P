using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace ShortP2P.Client.Routing;

internal static class LocalEndpointHelper
{
    public static string GetPreferredLanIPv4String()
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork)
                        continue;
                    if (IPAddress.IsLoopback(ua.Address))
                        continue;
                    return ua.Address.ToString();
                }
            }
        }
        catch
        {
            // ignore
        }

        return "127.0.0.1";
    }
}