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
        if (TryParseServiceDataNetworkIdHint(sectionPayload, serviceUuidBytes, out var hint))
            return new BleAdScanResult { NetworkIdHint = hint };

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

    private static bool TryParseServiceDataNetworkIdHint(ReadOnlySpan<byte> sectionPayload,
        ReadOnlySpan<byte> serviceUuidBytes, out byte[] hint)
    {
        hint = [];
        if (BleShortP2PGattProtocol.TryParseGattServiceDataNetworkIdHint(sectionPayload, out hint))
            return true;

        if (sectionPayload.Length < 16 + BleShortP2PGattProtocol.ManufacturerNetworkIdHintPayloadLength)
            return false;
        if (!sectionPayload.Slice(0, 16).SequenceEqual(serviceUuidBytes))
            return false;
        return BleShortP2PGattProtocol.TryParseGattServiceDataNetworkIdHint(sectionPayload.Slice(16), out hint);
    }

    public static BleAdScanResult Merge(BleAdScanResult current, BleAdScanResult next)
    {
        if (!next.HasIdentity)
            return current;
        if (!current.HasIdentity)
            return next;

        var hint = next.HasHint ? next.NetworkIdHint
            : current.HasHint ? current.NetworkIdHint : ReadOnlyMemory<byte>.Empty;
        var legacy = current.LegacyFullNetworkId ?? next.LegacyFullNetworkId;
        return new BleAdScanResult { NetworkIdHint = hint, LegacyFullNetworkId = legacy };
    }
}
