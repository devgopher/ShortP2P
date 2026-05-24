using System.Text;
using Microsoft.Extensions.Logging;
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

    public static void LogGattAdvertisingStarted(ILogger? logger, bool discoverable, Guid? networkId, int? serviceDataBytes)
    {
        if (logger == null)
            return;

        if (networkId is { } id)
        {
            Span<byte> hint = stackalloc byte[BleAdScanResult.NetworkIdHintLength];
            BleShortP2PGattProtocol.TryWriteNetworkIdHint(id, hint);
            logger.LogInformation(
                "BLE GATT advertising started (discoverable={Discoverable}, serviceDataBytes={ServiceDataBytes}, hintHex={HintHex})",
                discoverable, serviceDataBytes, Convert.ToHexString(hint));
        }
        else
            logger.LogInformation("BLE GATT advertising started (discoverable={Discoverable}, no NetworkId hint)",
                discoverable);
    }

    public static void LogManufacturerPublisherStarted(ILogger? logger, Guid networkId, int payloadBytes)
    {
        if (logger == null)
            return;

        Span<byte> hint = stackalloc byte[BleAdScanResult.NetworkIdHintLength];
        BleShortP2PGattProtocol.TryWriteNetworkIdHint(networkId, hint);
        logger.LogInformation(
            "BLE manufacturer publisher started (payloadBytes={PayloadBytes}, hintHex={HintHex}, companyId=0x{CompanyId:X4})",
            payloadBytes, Convert.ToHexString(hint), BleShortP2PGattProtocol.ManufacturerCompanyId);
    }

    public static void LogAdvertisementReceived(ILogger? logger, ulong bluetoothAddress, string macKey,
        BluetoothLEAdvertisementReceivedEventArgs args, BleAdScanResult merged)
    {
        if (logger == null)
            return;

        var ad = args.Advertisement;
        logger.LogDebug(
            "BLE adv {Mac} addrType={AddressType} advType={AdvertisementType} rssi={Rssi} svc={ServiceUuidCount} mfg={ManufacturerCount} sections={SectionCount} mergedHint={HasHint} mergedLegacy={HasLegacy}",
            macKey,
            args.BluetoothAddressType,
            args.AdvertisementType,
            args.RawSignalStrengthInDBm,
            ad.ServiceUuids.Count,
            ad.ManufacturerData.Count,
            ad.DataSections.Count,
            merged.HasHint,
            merged.LegacyFullNetworkId != null);

        if (logger.IsEnabled(LogLevel.Trace))
        {
            logger.LogTrace("BLE adv {Mac} detail: services=[{Services}] mfg=[{Manufacturer}] sections=[{Sections}] hintHex={HintHex}",
                macKey,
                FormatServiceUuids(ad),
                FormatManufacturerData(ad),
                FormatDataSections(ad),
                merged.HasHint ? Convert.ToHexString(merged.NetworkIdHint.Span) : "");
        }
    }

    public static void LogScanDiscovery(ILogger? logger, string macKey, BleAdScanResult merged, bool improved)
    {
        if (logger == null)
            return;

        logger.LogInformation(
            "BLE scan peer {Mac} hint={HasHint} legacy={HasLegacy} improved={Improved} hintHex={HintHex}",
            macKey,
            merged.HasHint,
            merged.LegacyFullNetworkId != null,
            improved,
            merged.HasHint ? Convert.ToHexString(merged.NetworkIdHint.Span) : "");
    }

    public static void LogIdentityMerged(ILogger? logger, ulong bluetoothAddress, BleAdScanResult merged)
    {
        if (logger == null || !merged.HasIdentity)
            return;

        logger.LogDebug(
            "BLE identity merged for {Address:X12}: hint={HasHint} hintHex={HintHex} legacy={Legacy}",
            bluetoothAddress,
            merged.HasHint,
            merged.HasHint ? Convert.ToHexString(merged.NetworkIdHint.Span) : "",
            merged.LegacyFullNetworkId);
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
