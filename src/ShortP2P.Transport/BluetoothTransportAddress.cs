using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Transport;

/// <summary>
///     MAC-адрес (6 байт) как адрес Bluetooth-транспорта (для будущей реализации).
/// </summary>
public static class BluetoothTransportAddress
{
    public const int MacLength = 6;

    public static TransportAddress FromMac(ReadOnlySpan<byte> mac6)
    {
        if (mac6.Length != MacLength) throw new ArgumentException($"MAC must be {MacLength} bytes.", nameof(mac6));
        return new TransportAddress(TransportKind.Bluetooth, mac6.ToArray());
    }
}