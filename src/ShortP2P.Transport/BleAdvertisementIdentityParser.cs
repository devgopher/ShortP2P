using ShortP2P.Auth.Data;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Transport;

/// <summary>Парсинг NetworkId из BLE manufacturer data (канонический канал ShortP2P).</summary>
public static class BleAdvertisementIdentityParser
{
    public const byte AdTypeServiceData128 = 0x21;

    public static bool IsShortP2P(bool advertisesShortP2PServiceUuid, IEnumerable<ushort>? manufacturerCompanyIds)
    {
        if (advertisesShortP2PServiceUuid)
            return true;

        if (manufacturerCompanyIds == null)
            return false;

        foreach (var companyId in manufacturerCompanyIds)
        {
            if (companyId == BleShortP2PGattProtocol.ManufacturerCompanyId)
                return true;
        }

        return false;
    }

    public static BleAdScanResult ParseManufacturerEntries(IEnumerable<BleManufacturerDataEntry> entries)
    {
        var result = default(BleAdScanResult);
        foreach (var entry in entries)
            result = Merge(result, ParseManufacturerData(entry.CompanyId, entry.Payload));
        return result;
    }

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

/// <summary>Manufacturer-specific data из BLE AD (company id + payload без AD-заголовка).</summary>
public readonly record struct BleManufacturerDataEntry(ushort CompanyId, byte[] Payload);
