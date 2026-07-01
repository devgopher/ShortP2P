using System.Globalization;
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
        return mac6.Length != MacLength
            ? throw new ArgumentException($"MAC must be {MacLength} bytes.", nameof(mac6))
            : new TransportAddress(TransportKind.Bluetooth, mac6.ToArray());
    }

    public static bool TryParseMac(string text, out byte[] mac6)
    {
        mac6 = [];
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var clean = text.Trim().Replace("-", "", StringComparison.Ordinal).Replace(":", "", StringComparison.Ordinal);
        if (clean.Length != MacLength * 2)
            return false;
        var bytes = new byte[MacLength];
        for (var i = 0; i < MacLength; i++)
            if (!byte.TryParse(clean.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                    out bytes[i]))
                return false;

        mac6 = bytes;
        return true;
    }

    public static string ToMacString(ReadOnlySpan<byte> mac6)
    {
        if (mac6.Length != MacLength) throw new ArgumentException($"MAC must be {MacLength} bytes.", nameof(mac6));
        return string.Create(MacLength * 3 - 1, mac6.ToArray(), static (span, bytes) =>
        {
            var pos = 0;
            for (var i = 0; i < bytes.Length; i++)
            {
                if (i > 0)
                    span[pos++] = ':';
                var b = bytes[i];
                span[pos++] = GetHex((byte)(b >> 4));
                span[pos++] = GetHex((byte)(b & 0x0F));
            }

            static char GetHex(byte v)
            {
                return (char)(v < 10 ? '0' + v : 'A' + (v - 10));
            }
        });
    }

    /// <summary>Лексикографическое сравнение MAC (6 байт); для tie-break при выборе инициатора BLE.</summary>
    public static int CompareMac(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        if (left.Length != MacLength)
            throw new ArgumentException($"MAC must be {MacLength} bytes.", nameof(left));
        if (right.Length != MacLength)
            throw new ArgumentException($"MAC must be {MacLength} bytes.", nameof(right));
        return left.SequenceCompareTo(right);
    }

    /// <summary>True, если этот узел должен инициировать исходящее BLE-подключение к пиру (локальный MAC строго больше).</summary>
    public static bool ShouldInitiateBleConnection(ReadOnlySpan<byte> localMac, ReadOnlySpan<byte> peerMac) =>
        CompareMac(localMac, peerMac) > 0;
}