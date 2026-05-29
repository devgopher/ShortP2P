using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using ShortP2P.Auth.Data;
using ShortP2P.Transport;
using ShortP2P.Transport.Abstractions;
using Windows.Devices.Bluetooth.Advertisement;

namespace ShortP2P.Transport.Bluetooth.Windows;

/// <summary>
///     WinRT отдаёт ADV и scan response отдельными событиями; manufacturer data часто не в пакете с ServiceUuid.
/// </summary>
internal sealed class BleAdvertisementMergeCache
{
    private readonly ILogger? _logger;
    private readonly ConcurrentDictionary<ulong, BleAdScanResult> _identityByAddress = new();

    public BleAdvertisementMergeCache(ILogger? logger = null)
    {
        _logger = logger;
    }

    public BleAdScanResult Observe(ulong bluetoothAddress, BluetoothLEAdvertisement advertisement)
    {
        _ = advertisement;
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
        var hasService = advertisement.ServiceUuids.Contains(BleShortP2PGattProtocol.ServiceUuid);
        IEnumerable<ushort>? companyIds = advertisement.ManufacturerData.Count > 0
            ? advertisement.ManufacturerData.Select(md => md.CompanyId)
            : null;
        return BleAdvertisementIdentityParser.IsShortP2P(hasService, companyIds);
    }

    public static bool IdentityImproved(BleAdScanResult previous, BleAdScanResult current)
    {
        return !previous.HasNetworkId && current.HasNetworkId;
    }
}
