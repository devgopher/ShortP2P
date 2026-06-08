using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using System.Text;
using ShortP2P.Auth.Data;
using ShortP2P.Client.Qr;
using ShortP2P.Client.Routing;
using ShortP2P.Discovery;

namespace ShortP2P.Client;

/// <summary>Сборка текста «мои транспорты» для буфера обмена: UDP-эндпоинты, Bluetooth MAC, serial (ИК).</summary>
public static class MyTransportEndpointsText
{
    public static string Build(UserEntity user, P2pRoutingSettings settings, string? bluetoothAdapterMac = null)
    {
        var sb = new StringBuilder();
        AppendUdp(sb, user, settings.EnableUdpTransport);
        AppendBluetooth(sb, settings.EnableBluetoothTransport, bluetoothAdapterMac);
        AppendInfrared(sb);
        return sb.ToString().TrimEnd('\r', '\n');
    }

    private static void AppendUdp(StringBuilder sb, UserEntity user, bool enabled)
    {
        sb.AppendLine("udp:");
        if (!enabled)
        {
            sb.AppendLine("(disabled)");
            return;
        }

        var lineSet = new HashSet<string>(StringComparer.Ordinal);
        var ips = CollectIpAddresses();
        var ports = new[]
        {
            user.DataUdpPort,
            PresencePingCodec.UdpPort,
            ChatInviteCodec.InviteUdpPort,
            UdpPeerDiscoveryOptions.DefaultDiscoveryUdpPort
        };

        foreach (var ip in ips)
        foreach (var port in ports)
            lineSet.Add(FormatHostPort(ip, port));

        foreach (var line in lineSet.OrderBy(x => x, StringComparer.Ordinal))
            sb.AppendLine(line);
    }

    private static void AppendBluetooth(StringBuilder sb, bool enabled, string? mac)
    {
        sb.AppendLine("bluetooth:");
        if (!enabled)
        {
            sb.AppendLine("(disabled)");
            return;
        }

        if (string.IsNullOrWhiteSpace(mac))
            sb.AppendLine("(unavailable)");
        else
            sb.AppendLine(mac.Trim());
    }

    private static void AppendInfrared(StringBuilder sb)
    {
        sb.AppendLine("infrared:");
        try
        {
            var ports = SerialPort.GetPortNames();
            if (ports.Length == 0)
            {
                sb.AppendLine("(no serial ports)");
                return;
            }

            foreach (var p in ports.OrderBy(x => x, StringComparer.Ordinal))
                sb.AppendLine(p);
        }
        catch
        {
            sb.AppendLine("(unavailable)");
        }
    }

    private static List<string> CollectIpAddresses()
    {
        var ordered = new List<string>();
        var pub = LocalIPv4Resolver.TryGetPublicIpv4(TimeSpan.FromSeconds(1));
        if (!string.IsNullOrWhiteSpace(pub))
            ordered.Add(pub.Trim());

        foreach (var ip in LocalIPv4Resolver.GetAllUnicastIpv4Ordered())
        {
            if (ordered.Contains(ip, StringComparer.OrdinalIgnoreCase))
                continue;
            ordered.Add(ip);
        }

        foreach (var ip in LocalIPv4Resolver.GetAllUnicastIpv6Ordered())
        {
            if (ordered.Contains(ip, StringComparer.OrdinalIgnoreCase))
                continue;
            ordered.Add(ip);
        }

        if (!ordered.Contains("127.0.0.1", StringComparer.OrdinalIgnoreCase))
            ordered.Add("127.0.0.1");
        if (!ordered.Contains("::1", StringComparer.OrdinalIgnoreCase))
            ordered.Add("::1");

        return ordered;
    }

    private static string FormatHostPort(string host, int port)
    {
        if (!IPAddress.TryParse(host, out var ip))
            return $"{host}:{port}";
        return ip.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{host}]:{port}" : $"{host}:{port}";
    }
}