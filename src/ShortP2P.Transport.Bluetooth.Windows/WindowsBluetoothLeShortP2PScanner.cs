using System.Runtime.Versioning;
using Windows.Devices.Bluetooth.Advertisement;
using Microsoft.Extensions.Logging;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Transport.Bluetooth.Windows;

/// <summary>
///     WinRT: наблюдатель BLE-рекламы ShortP2P (active scan, merge ADV + scan response).
/// </summary>
[SupportedOSPlatform("windows10.0.18362.0")]
public sealed class WindowsBluetoothLeShortP2PScanner(ILogger<WindowsBluetoothLeShortP2PScanner>? logger = null)
    : IBleShortP2PPeripheralScanner
{
    // private readonly BleAdvertisementMergeCache _mergeCache = new(logger);

    public async Task ScanAsync(TimeSpan duration, Action<TransportAddress, BleAdScanResult> onDeviceDiscovered,
        CancellationToken cancellationToken = default)
    {
        if (duration <= TimeSpan.Zero)
            return;

        logger?.LogInformation("BLE peripheral scan starting for {Duration}", duration);

        var watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Passive,
            SignalStrengthFilter =
            {
                SamplingInterval = TimeSpan.FromMilliseconds(500)
            }
        };
        var seen = new Dictionary<ulong, BleAdScanResult>();
        watcher.Received += (_, e) =>
        {
            // if (!BleWindowsAdvertisementHelper.IsShortP2P(e.Advertisement))
            //     return;
            // var scanResult = _mergeCache.Observe(e.BluetoothAddress, e.Advertisement);
            // var hadEntry = seen.TryGetValue(e.BluetoothAddress, out var prev);
            // if (hadEntry && !BleWindowsAdvertisementHelper.IdentityImproved(prev, scanResult))
            //     return;
            // seen[e.BluetoothAddress] = scanResult;
            // var mac = BluetoothMacAddress.FromBluetoothAddress(e.BluetoothAddress);
            // var macKey = BluetoothTransportAddress.ToMacString(mac);
            // BleWindowsAdvertisementLog.LogScanDiscovery(logger, macKey, scanResult, !hadEntry);
            // onDeviceDiscovered(BluetoothTransportAddress.FromMac(mac), scanResult);
        };

        try
        {
            watcher.Start();
            await Task.Delay(duration, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            watcher.Stop();
            logger?.LogInformation("BLE peripheral scan stopped");
        }
    }
}