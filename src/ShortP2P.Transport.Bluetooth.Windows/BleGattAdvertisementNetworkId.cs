using ShortP2P.Transport.Abstractions;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Storage.Streams;

namespace ShortP2P.Transport.Bluetooth.Windows;

/// <summary>Парсинг полного NetworkId из BLE-рекламы ShortP2P.</summary>
internal static class BleGattAdvertisementNetworkId
{
    public static BleAdScanResult TryParseFromAdvertisement(BluetoothLEAdvertisement advertisement)
    {
        var result = default(BleAdScanResult);
        foreach (var md in advertisement.ManufacturerData)
        {
            var bytes = ReadBuffer(md.Data);
            result = BleAdvertisementIdentityParser.Merge(result,
                BleAdvertisementIdentityParser.ParseManufacturerData(md.CompanyId, bytes));
        }

        if (result.HasIdentity)
            return result;

        var uuidBytes = new byte[16];
        if (!BleShortP2PGattProtocol.ServiceUuid.TryWriteBytes(uuidBytes))
            return result;

        var serviceAdvertised = advertisement.ServiceUuids.Contains(BleShortP2PGattProtocol.ServiceUuid);
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
