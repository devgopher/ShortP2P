using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Transport;

/// <summary>Парсинг NetworkId / hint из BLE manufacturer data и service data.</summary>
public static class BleAdvertisementIdentityParser
{
    public const byte AdTypeServiceData128 = 0x21;

    public static BleAdScanResult ParseManufacturerData(ushort companyId, ReadOnlySpan<byte> data)
    {
        if (BleShortP2PGattProtocol.TryParseManufacturerNetworkIdHint(companyId, data, out var hint))
        {
            return new BleAdScanResult { NetworkIdHint = hint };
        }

        if (BleShortP2PGattProtocol.TryParseManufacturerNetworkId(companyId, data, out var legacyFull))
        {
            Span<byte> derivedHint = stackalloc byte[BleAdScanResult.NetworkIdHintLength];
            BleShortP2PGattProtocol.TryWriteNetworkIdHint(legacyFull, derivedHint);
            return new BleAdScanResult
            {
                NetworkIdHint = derivedHint.ToArray(),
                LegacyFullNetworkId = legacyFull,
            };
        }

        return default;
    }

    public static BleAdScanResult ParseServiceDataSection(ReadOnlySpan<byte> sectionPayload,
        ReadOnlySpan<byte> serviceUuidBytes, bool serviceUuidAdvertised)
    {
        if (!BleShortP2PGattProtocol.TryParseAdvertisementServiceDataSection(sectionPayload, serviceUuidBytes,
                serviceUuidAdvertised, out var legacyFull))
            return default;

        Span<byte> derivedHint = stackalloc byte[BleAdScanResult.NetworkIdHintLength];
        BleShortP2PGattProtocol.TryWriteNetworkIdHint(legacyFull, derivedHint);
        return new BleAdScanResult
        {
            NetworkIdHint = derivedHint.ToArray(),
            LegacyFullNetworkId = legacyFull,
        };
    }

    public static BleAdScanResult Merge(BleAdScanResult current, BleAdScanResult next)
    {
        if (!next.HasIdentity)
            return current;
        if (!current.HasIdentity)
            return next;
        if (current.LegacyFullNetworkId == null && next.LegacyFullNetworkId is { } full)
            return new BleAdScanResult { NetworkIdHint = next.NetworkIdHint, LegacyFullNetworkId = full };
        return current;
    }
}
