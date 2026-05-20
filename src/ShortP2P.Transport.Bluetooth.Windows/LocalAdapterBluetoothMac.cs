using System.Runtime.Versioning;
using Windows.Devices.Bluetooth;
using TransportAddressFmt = ShortP2P.Transport.BluetoothTransportAddress;

namespace ShortP2P.Transport.Bluetooth.Windows;

/// <summary>Публичный MAC локального Bluetooth-адаптера (best-effort).</summary>
[SupportedOSPlatform("windows10.0.17763.0")]
public static class LocalAdapterBluetoothMac
{
    public static async Task<ulong?> TryGetAdapterAddressAsync()
    {
        try
        {
            var adapter = await BluetoothAdapter.GetDefaultAsync();
            if (adapter == null)
                return null;
            return adapter.BluetoothAddress;
        }
        catch
        {
            return null;
        }
    }
    
    public static async Task<string?> TryGetAdapterMacStringAsync()
    {
        try
        {
            var adapter = await BluetoothAdapter.GetDefaultAsync();
            if (adapter == null)
                return null;
            var mac = BluetoothMacAddress.FromBluetoothAddress(adapter.BluetoothAddress);
            return TransportAddressFmt.ToMacString(mac);
        }
        catch
        {
            return null;
        }
    }
}
