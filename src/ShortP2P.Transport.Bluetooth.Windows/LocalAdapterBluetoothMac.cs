using System.Runtime.Versioning;
using Windows.Devices.Bluetooth;
using TransportAddressFmt = ShortP2P.Transport.BluetoothTransportAddress;

namespace ShortP2P.Transport.Bluetooth.Windows;

/// <summary>Публичный MAC локального Bluetooth-адаптера (best-effort).</summary>
[SupportedOSPlatform("windows10.0.17763.0")]
public static class LocalAdapterBluetoothMac
{
    public static async Task<ulong?> TryGetAdapterAddressAsync(string? deviceId = null)
    {
        try
        {
            var adapter = await ResolveAdapterAsync(deviceId).ConfigureAwait(false);
            return adapter?.BluetoothAddress;
        }
        catch
        {
            return null;
        }
    }

    public static async Task<string?> TryGetAdapterMacStringAsync(string? deviceId = null)
    {
        try
        {
            var addr = await TryGetAdapterAddressAsync(deviceId).ConfigureAwait(false);
            if (addr is null or 0)
                return null;
            var mac = BluetoothMacAddress.FromBluetoothAddress(addr.Value);
            return TransportAddressFmt.ToMacString(mac);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<BluetoothAdapter?> ResolveAdapterAsync(string? deviceId)
    {
        if (!string.IsNullOrWhiteSpace(deviceId))
            try
            {
                return await BluetoothAdapter.FromIdAsync(deviceId).AsTask().ConfigureAwait(false);
            }
            catch
            {
                // fall through
            }

        return await BluetoothAdapter.GetDefaultAsync().AsTask().ConfigureAwait(false);
    }
}