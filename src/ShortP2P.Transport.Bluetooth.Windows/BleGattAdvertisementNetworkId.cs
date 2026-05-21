using ShortP2P.Transport;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Storage.Streams;

namespace ShortP2P.Transport.Bluetooth.Windows;

/// <summary>Парсинг NetworkId из BLE-рекламы ShortP2P (Manufacturer Data и GATT Service Data AD 0x21).</summary>
internal static class BleGattAdvertisementNetworkId
{
    private const byte AdTypeServiceData128 = 0x21;

    public static Guid? TryParseFromAdvertisement(BluetoothLEAdvertisement advertisement)
    {
        foreach (var md in advertisement.ManufacturerData)
        {
            var bytes = ReadBuffer(md.Data);
            if (BleShortP2PGattProtocol.TryParseManufacturerNetworkId((ushort)md.CompanyId, bytes, out var fromMfg))
                return fromMfg;
        }

        var uuidBytes = new byte[16];
        if (!BleShortP2PGattProtocol.ServiceUuid.TryWriteBytes(uuidBytes))
            return null;

        var serviceAdvertised = advertisement.ServiceUuids.Contains(BleShortP2PGattProtocol.ServiceUuid);

        foreach (var section in advertisement.DataSections)
        {
            if (section.DataType != AdTypeServiceData128)
                continue;
            var payload = ReadBuffer(section.Data);
            if (BleShortP2PGattProtocol.TryParseAdvertisementServiceDataSection(payload, uuidBytes, serviceAdvertised,
                    out var id))
                return id;
        }

        return null;
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
