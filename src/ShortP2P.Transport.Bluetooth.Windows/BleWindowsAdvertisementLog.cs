using System.Text;
using Microsoft.Extensions.Logging;
using ShortP2P.Auth.Data;
using ShortP2P.Transport;
using ShortP2P.Transport.Abstractions;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Storage.Streams;

namespace ShortP2P.Transport.Bluetooth.Windows;

internal static class BleWindowsAdvertisementLog
{
    public static void LogPublisherStatus(ILogger? logger, BluetoothLEAdvertisementPublisherStatus status)
    {
        if (logger == null)
            return;

        if (status is BluetoothLEAdvertisementPublisherStatus.Aborted
            or BluetoothLEAdvertisementPublisherStatus.Stopped)
            logger.LogWarning("BLE manufacturer publisher status: {Status}", status);
        else
            logger.LogInformation("BLE manufacturer publisher status: {Status}", status);
    }

    public static void LogGattAdvertisingStarted(ILogger? logger, bool discoverable, CompressedNetworkId? networkId, int? serviceDataBytes)
    {
        if (logger == null)
            return;

        if (networkId is { } id && !id.IsEmpty)
        {
            logger.LogInformation(
                "BLE GATT advertising started (discoverable={Discoverable}, serviceDataBytes={ServiceDataBytes}, networkId={NetworkId})",
                discoverable, serviceDataBytes, id.ToShortString());
        }
        else
            logger.LogInformation("BLE GATT advertising started (discoverable={Discoverable}, no NetworkId)",
                discoverable);
    }

    public static void LogManufacturerPublisherStarted(ILogger? logger, CompressedNetworkId networkId, int payloadBytes)
    {
        if (logger == null)
            return;

        logger.LogInformation(
            "BLE manufacturer publisher started (payloadBytes={PayloadBytes}, networkId={NetworkId}, companyId=0x{CompanyId:X4})",
            payloadBytes, networkId.ToShortString(), BleShortP2PGattProtocol.ManufacturerCompanyId);
    }

    public static void LogAdvertisementReceived(ILogger? logger, ulong bluetoothAddress, string macKey,
        BluetoothLEAdvertisementReceivedEventArgs args, BleAdScanResult merged)
    {
        if (logger == null)
            return;

        var ad = args.Advertisement;
        logger.LogDebug(
            "BLE adv {Mac} addrType={AddressType} advType={AdvertisementType} rssi={Rssi} svc={ServiceUuidCount} mfg={ManufacturerCount} sections={SectionCount} mergedNetworkId={HasNetworkId}",
            macKey,
            args.BluetoothAddressType,
            args.AdvertisementType,
            args.RawSignalStrengthInDBm,
            ad.ServiceUuids.Count,
            ad.ManufacturerData.Count,
            ad.DataSections.Count,
            merged.HasNetworkId);

        if (logger.IsEnabled(LogLevel.Trace))
        {
            logger.LogTrace("BLE adv {Mac} detail: services=[{Services}] mfg=[{Manufacturer}] sections=[{Sections}] networkId={NetworkId}",
                macKey,
                FormatServiceUuids(ad),
                FormatManufacturerData(ad),
                FormatDataSections(ad),
                merged.NetworkId);
        }
    }

    public static void LogScanDiscovery(ILogger? logger, string macKey, BleAdScanResult merged, bool improved)
    {
        if (logger == null)
            return;

        logger.LogInformation(
            "BLE scan peer {Mac} networkId={NetworkId} improved={Improved}",
            macKey,
            merged.NetworkId,
            improved);
    }

    public static void LogIdentityMerged(ILogger? logger, ulong bluetoothAddress, BleAdScanResult merged)
    {
        if (logger == null || !merged.HasIdentity)
            return;

        logger.LogDebug(
            "BLE identity merged for {Address:X12}: networkId={NetworkId}",
            bluetoothAddress,
            merged.NetworkId);
    }

    private static string FormatServiceUuids(BluetoothLEAdvertisement ad)
    {
        if (ad.ServiceUuids.Count == 0)
            return "";
        return string.Join(", ", ad.ServiceUuids.Select(u => u.ToString("D")));
    }

    private static string FormatManufacturerData(BluetoothLEAdvertisement ad)
    {
        if (ad.ManufacturerData.Count == 0)
            return "";
        var sb = new StringBuilder();
        foreach (var md in ad.ManufacturerData)
        {
            var bytes = ReadBuffer(md.Data);
            sb.Append("0x").Append(md.CompanyId.ToString("X4")).Append('[').Append(bytes.Length).Append("]=")
                .Append(Convert.ToHexString(bytes)).Append("; ");
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatDataSections(BluetoothLEAdvertisement ad)
    {
        if (ad.DataSections.Count == 0)
            return "";
        var sb = new StringBuilder();
        foreach (var section in ad.DataSections)
        {
            var bytes = ReadBuffer(section.Data);
            sb.Append("0x").Append(section.DataType.ToString("X2")).Append('[').Append(bytes.Length).Append("]=")
                .Append(Convert.ToHexString(bytes)).Append("; ");
        }

        return sb.ToString().TrimEnd();
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
