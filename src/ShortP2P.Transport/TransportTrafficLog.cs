using System.Text;
using Microsoft.Extensions.Logging;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Transport;

/// <summary>Форматирование и запись wire-трафика транспорта (hex payload, адреса UDP/BLE). Hex — только DEBUG.</summary>
public static class TransportTrafficLog
{
    public static string FormatPayloadHex(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty)
            return "(empty)";

        var sb = new StringBuilder(payload.Length * 5);
        for (var i = 0; i < payload.Length; i++)
        {
            if (i > 0)
                sb.Append(' ');
            sb.Append("0x").Append(payload[i].ToString("X2"));
        }

        return sb.ToString();
    }

    public static string FormatAddress(TransportAddress address)
    {
        try
        {
            return address.Kind switch
            {
                TransportKind.Udp => UdpTransportAddress.ToIPEndPoint(address).ToString()!,
                TransportKind.Bluetooth => BluetoothTransportAddress.ToMacString(address.Data),
                _ => address.Kind.ToString()
            };
        }
        catch
        {
            return address.Kind.ToString();
        }
    }

    public static void LogReceive(ILogger? logger, TransportAddress remote, string localEndpoint,
        ReadOnlySpan<byte> payload)
    {
#if DEBUG
        if (logger?.IsEnabled(LogLevel.Debug) != true)
            return;

        logger.LogDebug("RX {Remote} -> {Local}: {Payload}",
            FormatAddress(remote), localEndpoint, FormatPayloadHex(payload));
#endif
    }

    public static void LogSend(ILogger? logger, string localEndpoint, TransportAddress remote,
        ReadOnlySpan<byte> payload)
    {
#if DEBUG
        if (logger?.IsEnabled(LogLevel.Debug) != true)
            return;

        logger.LogDebug("TX {Local} -> {Remote}: {Payload}",
            localEndpoint, FormatAddress(remote), FormatPayloadHex(payload));
#endif
    }
}
