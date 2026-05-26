using ShortP2P.Auth.Data;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Transport;

/// <summary>Парсинг NetworkId из BLE manufacturer data и service data.</summary>
public static class BleAdvertisementIdentityParser
{
    public const byte AdTypeServiceData128 = 0x21;

    public static BleAdScanResult ParseManufacturerData(ushort companyId, ReadOnlySpan<byte> data)
    {
        if (BleShortP2PGattProtocol.TryParseManufacturerNetworkIdPayload(companyId, data, out var networkId)
            || BleShortP2PGattProtocol.TryParseManufacturerLegacyNetworkId(companyId, data, out networkId))
        {
            return new BleAdScanResult { NetworkId = networkId };
        }

        return default;
    }

    public static BleAdScanResult ParseServiceDataSection(ReadOnlySpan<byte> sectionPayload,
        ReadOnlySpan<byte> serviceUuidBytes, bool serviceUuidAdvertised)
    {
        if (!BleShortP2PGattProtocol.TryParseAdvertisementServiceDataSection(sectionPayload, serviceUuidBytes,
                serviceUuidAdvertised, out var networkId))
            return default;

        return new BleAdScanResult { NetworkId = networkId };
    }

    public static BleAdScanResult Merge(BleAdScanResult current, BleAdScanResult next)
    {
        if (!next.HasIdentity)
            return current;
        if (!current.HasIdentity)
            return next;

        return new BleAdScanResult { NetworkId = current.NetworkId ?? next.NetworkId };
    }
}
