using Windows.Devices.Bluetooth.Advertisement;
using Windows.Storage.Streams;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Transport.Bluetooth.Windows;

/// <summary>Парсинг NetworkId из BLE-рекламы ShortP2P (manufacturer data, затем legacy service data).</summary>
internal static class BleGattAdvertisementNetworkId
{
    public static BleAdScanResult TryParseFromAdvertisement(BluetoothLEAdvertisement advertisement)
    {
        if (advertisement.ManufacturerData.Count > 0)
        {
            var entries = new BleManufacturerDataEntry[advertisement.ManufacturerData.Count];
            var i = 0;
            foreach (var md in advertisement.ManufacturerData)
                entries[i++] = new BleManufacturerDataEntry(md.CompanyId, ReadBuffer(md.Data));

            var fromManufacturer = BleAdvertisementIdentityParser.ParseManufacturerEntries(entries);
            if (fromManufacturer.HasIdentity)
                return fromManufacturer;
        }

        if (advertisement.DataSections.Count == 0)
            return default;

        Span<byte> uuidBytes = stackalloc byte[16];
        if (!BleShortP2PGattProtocol.ServiceUuid.TryWriteBytes(uuidBytes))
            return default;

        var serviceAdvertised = advertisement.ServiceUuids.Contains(BleShortP2PGattProtocol.ServiceUuid);
        var result = default(BleAdScanResult);
        foreach (var section in advertisement.DataSections)
        {
            if (section.DataType != BleAdvertisementIdentityParser.AdTypeServiceData128)
                continue;
            var payload = ReadBuffer(section.Data);
            result = BleAdvertisementIdentityParser.Merge(result,
                BleAdvertisementIdentityParser.ParseServiceDataSection(payload, uuidBytes, serviceAdvertised));
            if (result.HasIdentity)
                return result;
        }

        return result;
    }

    private static byte[] ReadBuffer(IBuffer buffer)
    {
        var length = (int)buffer.Length;
        if (length <= 0)
            return [];
        var bytes = new byte[length];
        DataReader.FromBuffer(buffer).ReadBytes(bytes);
        return bytes;
    }
}