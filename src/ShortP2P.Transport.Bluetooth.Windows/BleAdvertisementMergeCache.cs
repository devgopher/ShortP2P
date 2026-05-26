using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
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
        var parsed = BleGattAdvertisementNetworkId.TryParseFromAdvertisement(advertisement);
        if (!parsed.HasIdentity)
        {
            return _identityByAddress.TryGetValue(bluetoothAddress, out var prev) ? prev : default;
        }

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
        if (advertisement.ServiceUuids.Contains(BleShortP2PGattProtocol.ServiceUuid))
            return true;

        foreach (var md in advertisement.ManufacturerData)
        {
            if (md.CompanyId == BleShortP2PGattProtocol.ManufacturerCompanyId)
                return true;
        }

        return false;
    }

    public static bool IdentityImproved(BleAdScanResult previous, BleAdScanResult current)
    {
        return !previous.HasNetworkId && current.HasNetworkId;
    }
}
