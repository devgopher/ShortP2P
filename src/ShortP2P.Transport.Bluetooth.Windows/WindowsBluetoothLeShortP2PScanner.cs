using System.Runtime.Versioning;
using ShortP2P.Transport;
using ShortP2P.Transport.Abstractions;
using Windows.Devices.Bluetooth.Advertisement;

namespace ShortP2P.Transport.Bluetooth.Windows;

/// <summary>
///     WinRT: наблюдатель BLE-рекламы с фильтром по сервису ShortP2P.
/// </summary>
[SupportedOSPlatform("windows10.0.18362.0")]
public sealed class WindowsBluetoothLeShortP2PScanner : IBleShortP2PPeripheralScanner
{
    public async Task ScanAsync(TimeSpan duration, Action<TransportAddress> onDeviceDiscovered,
        CancellationToken cancellationToken = default)
    {
        if (duration <= TimeSpan.Zero)
            return;

        var watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active,
            SignalStrengthFilter =
            {
                SamplingInterval = TimeSpan.FromMilliseconds(500)
            },
        };
        var filter = new BluetoothLEAdvertisementFilter();
        filter.Advertisement.ServiceUuids.Add(BleShortP2PGattProtocol.ServiceUuid);
        watcher.AdvertisementFilter = filter;

        var seen = new HashSet<ulong>();
        watcher.Received += (_, e) =>
        {
            if (e.Advertisement.ServiceUuids.Any())
            {
                if (!seen.Add(e.BluetoothAddress))
                    return;
                var mac = BluetoothMacAddress.FromBluetoothAddress(e.BluetoothAddress);
                onDeviceDiscovered(BluetoothTransportAddress.FromMac(mac));
            }
        };
            

        try
        {
            watcher.Start();
            await Task.Delay(duration, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            watcher.Stop();
        }
    }
}
