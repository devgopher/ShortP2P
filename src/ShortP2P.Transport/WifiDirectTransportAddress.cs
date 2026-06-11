using System.Text;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Transport;

/// <summary>
///     Адрес Wi-Fi Direct пира: нормализованный <c>DeviceInformation.Id</c> (UTF-8).
/// </summary>
public static class WifiDirectTransportAddress
{
    public static TransportAddress FromAddress(string deviceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        return new TransportAddress(TransportKind.WifiDirect, Encoding.UTF8.GetBytes(deviceId.Trim()));
    }

    public static string ToAddressString(ReadOnlySpan<byte> data)
    {
        return Encoding.UTF8.GetString(data);
    }

    public static bool TryParseAddress(ReadOnlySpan<byte> data, out string deviceId)
    {
        deviceId = Encoding.UTF8.GetString(data);
        return !string.IsNullOrWhiteSpace(deviceId);
    }
}
