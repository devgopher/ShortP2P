namespace ShortP2P.Transport.Bluetooth.Windows;

internal static class BluetoothMacAddress
{
    public const int MacLength = 6;

    public static ulong ToBluetoothAddress(ReadOnlySpan<byte> mac6)
    {
        if (mac6.Length != MacLength) throw new ArgumentException($"MAC must be {MacLength} bytes.", nameof(mac6));
        ulong u = 0;
        for (var i = 0; i < MacLength; i++) u |= (ulong)mac6[i] << (8 * i);
        return u;
    }

    public static byte[] FromBluetoothAddress(ulong address)
    {
        var mac = new byte[MacLength];
        for (var i = 0; i < MacLength; i++) mac[i] = (byte)(address >> (8 * i));
        return mac;
    }
}
