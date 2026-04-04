using System.Net;
using System.Net.Sockets;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Transport;

/// <summary>
///     Кодирование UDP-адреса в <see cref="TransportAddress.Data" /> и обратно.
///     Формат: [family:1][addr:4|16][port:2 BE].
/// </summary>
public static class UdpTransportAddress
{
    private const byte FamilyIPv4 = 4;
    private const byte FamilyIPv6 = 6;

    public static TransportAddress FromIPEndPoint(IPEndPoint ep)
    {
        if (ep.AddressFamily == AddressFamily.InterNetwork)
        {
            var ip = ep.Address.GetAddressBytes();
            if (ip.Length != 4) throw new ArgumentException("Invalid IPv4 address.", nameof(ep));
            var data = new byte[1 + 4 + 2];
            data[0] = FamilyIPv4;
            Buffer.BlockCopy(ip, 0, data, 1, 4);
            WriteUInt16Be(data, 5, (ushort)ep.Port);
            return new TransportAddress(TransportKind.Udp, data);
        }

        if (ep.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var ip = ep.Address.GetAddressBytes();
            if (ip.Length != 16) throw new ArgumentException("Invalid IPv6 address.", nameof(ep));
            var data = new byte[1 + 16 + 2];
            data[0] = FamilyIPv6;
            Buffer.BlockCopy(ip, 0, data, 1, 16);
            WriteUInt16Be(data, 17, (ushort)ep.Port);
            return new TransportAddress(TransportKind.Udp, data);
        }

        throw new NotSupportedException($"Address family {ep.AddressFamily} is not supported.");
    }

    public static IPEndPoint ToIPEndPoint(TransportAddress address)
    {
        if (address.Kind != TransportKind.Udp)
            throw new ArgumentException("Address is not UDP.", nameof(address));

        var data = address.Data;
        if (data.Length < 1 + 2) throw new ArgumentException("UDP address data is too short.", nameof(address));

        var family = data[0];
        if (family == FamilyIPv4)
        {
            if (data.Length != 1 + 4 + 2)
                throw new ArgumentException("Invalid IPv4 UDP address length.", nameof(address));
            var ip = new IPAddress(data.AsSpan(1, 4));
            var port = ReadUInt16Be(data, 5);
            return new IPEndPoint(ip, port);
        }

        if (family == FamilyIPv6)
        {
            if (data.Length != 1 + 16 + 2)
                throw new ArgumentException("Invalid IPv6 UDP address length.", nameof(address));
            var ip = new IPAddress(data.AsSpan(1, 16));
            var port = ReadUInt16Be(data, 17);
            return new IPEndPoint(ip, port);
        }

        throw new ArgumentException($"Unknown address family byte {family}.", nameof(address));
    }

    private static void WriteUInt16Be(byte[] buffer, int offset, ushort value)
    {
        buffer[offset] = (byte)(value >> 8);
        buffer[offset + 1] = (byte)value;
    }

    private static int ReadUInt16Be(byte[] buffer, int offset)
    {
        return (buffer[offset] << 8) | buffer[offset + 1];
    }
}