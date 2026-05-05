using System.Net;

namespace ShortP2P.Transport.Abstractions;

/// <summary>
///     Адрес пира в рамках конкретного транспорта. Содержимое <see cref="Data" /> задаёт реализация транспорта.
/// </summary>
public sealed class TransportAddress(TransportKind kind, byte[] data)
{
    private const byte UdpFamilyIPv4 = 4;
    private const byte UdpFamilyIPv6 = 6;

    public TransportKind Kind { get; } = kind;

    public byte[] Data { get; } = data ?? throw new ArgumentNullException(nameof(data));

    public string ToIpAddress()
    {
        if (Kind != TransportKind.Udp)
            throw new InvalidOperationException("IP address is available only for UDP transport address.");

        if (Data.Length < 3)
            throw new InvalidOperationException("UDP transport address payload is too short.");

        return Data[0] switch
        {
            UdpFamilyIPv4 when Data.Length == 7 => new IPAddress(Data.AsSpan(1, 4)).ToString(),
            UdpFamilyIPv6 when Data.Length == 19 => new IPAddress(Data.AsSpan(1, 16)).ToString(),
            UdpFamilyIPv4 => throw new InvalidOperationException("Invalid UDP IPv4 transport address length."),
            UdpFamilyIPv6 => throw new InvalidOperationException("Invalid UDP IPv6 transport address length."),
            _ => throw new InvalidOperationException("Unknown UDP address family.")
        };
    }
}