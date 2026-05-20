using ShortP2P.Transport;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Storage.Streams;

namespace ShortP2P.Transport.Bluetooth.Windows;

/// <summary>Парсинг NetworkId из BLE-рекламы ShortP2P (GATT Service Data AD type 0x21).</summary>
internal static class BleGattAdvertisementNetworkId
{
    private const byte AdTypeServiceData128 = 0x21;

    public static Guid? TryParseFromAdvertisement(BluetoothLEAdvertisement advertisement)
    {
        var uuidBytes = new byte[16];
        if (!BleShortP2PGattProtocol.ServiceUuid.TryWriteBytes(uuidBytes))
            return null;

        foreach (var section in advertisement.DataSections)
        {
            if (section.DataType != AdTypeServiceData128)
                continue;
            var payload = ReadBuffer(section.Data);
            if (payload.Length < 16 + BleShortP2PGattProtocol.GattServiceDataNetworkIdLength)
                continue;
            if (!payload.AsSpan(0, 16).SequenceEqual(uuidBytes))
                continue;
            if (BleShortP2PGattProtocol.TryParseGattServiceDataNetworkId(payload.AsSpan(16), out var id))
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
