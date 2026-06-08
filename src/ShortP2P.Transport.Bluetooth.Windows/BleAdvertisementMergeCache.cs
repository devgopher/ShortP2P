using System.Collections.Concurrent;
using Windows.Devices.Bluetooth.Advertisement;
using Microsoft.Extensions.Logging;
using ShortP2P.Auth.Data;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Transport.Bluetooth.Windows;

/// <summary>
///     WinRT отдаёт ADV и scan response отдельными событиями; manufacturer data часто не в пакете с ServiceUuid.
/// </summary>
internal sealed class BleAdvertisementMergeCache
{
    private readonly ConcurrentDictionary<ulong, BleAdScanResult> _identityByAddress = new();
    private readonly ILogger? _logger;

    public BleAdvertisementMergeCache(ILogger? logger = null)
    {
        _logger = logger;
    }

    public BleAdScanResult Observe(ulong bluetoothAddress, BluetoothLEAdvertisement advertisement)
    {
        return _identityByAddress.GetValueOrDefault(bluetoothAddress);
    }

    public BleAdScanResult RecordGattNetworkId(ulong bluetoothAddress, CompressedNetworkId networkId)
    {
        if (networkId.IsEmpty)
            return _identityByAddress.GetValueOrDefault(bluetoothAddress);

        var parsed = new BleAdScanResult { NetworkId = networkId };
        var merged = _identityByAddress.AddOrUpdate(bluetoothAddress, parsed,
            (_, existing) => BleAdvertisementIdentityParser.Merge(existing, parsed));
        BleWindowsAdvertisementLog.LogIdentityMerged(_logger, bluetoothAddress, merged);
        return merged;
    }
}

internal static class BleWindowsAdvertisementHelper
{
    public static bool IsShortP2P(BluetoothLEAdvertisement advertisement)
    {
        return advertisement.ServiceUuids.Contains(BleShortP2PGattProtocol.ServiceUuid);
    }

    public static bool IdentityImproved(BleAdScanResult previous, BleAdScanResult current)
    {
        return !previous.HasNetworkId && current.HasNetworkId;
    }
}