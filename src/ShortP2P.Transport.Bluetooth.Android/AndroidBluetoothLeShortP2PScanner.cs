using Android.Bluetooth;
using Android.Bluetooth.LE;
using Android.Content;
using Android.OS;
using Java.Util;
using ShortP2P.Transport;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Transport.Bluetooth.Android;

/// <summary>
///     Сканирование BLE-рекламы с UUID сервиса ShortP2P.
/// </summary>
public sealed class AndroidBluetoothLeShortP2PScanner(Context context) : IBleShortP2PPeripheralScanner
{
    private static readonly UUID ServiceUuidJava = UUID.FromString(BleShortP2PGattProtocol.ServiceUuid.ToString("D"));

    private readonly Context _context = context.ApplicationContext ?? context;

    public async Task ScanAsync(TimeSpan duration, Action<TransportAddress, BleAdScanResult> onDeviceDiscovered,
        CancellationToken cancellationToken = default)
    {
        if (duration <= TimeSpan.Zero)
            return;

        var manager = (BluetoothManager?)_context.GetSystemService(Context.BluetoothService);
        var adapter = manager?.Adapter;
        var scanner = adapter?.BluetoothLeScanner;
        if (scanner == null || adapter is not { IsEnabled: true })
            return;

        var callback = new LeScanCallbackImpl(onDeviceDiscovered);
        var filter = new ScanFilter.Builder()!.SetServiceUuid(new ParcelUuid(ServiceUuidJava))!.Build();
        var settings = new ScanSettings.Builder()!.SetScanMode(global::Android.Bluetooth.LE.ScanMode.LowLatency)!
            .Build();
        var filters = new List<ScanFilter> { filter };

        try
        {
            scanner.StartScan(filters, settings, callback);
        }
        catch
        {
            return;
        }

        try
        {
            await Task.Delay(duration, cancellationToken).ConfigureAwait(false);
        }
        catch (System.OperationCanceledException)
        {
            // expected
        }
        finally
        {
            try
            {
                scanner.StopScan(callback);
            }
            catch
            {
                // ignore
            }
        }
    }

    private sealed class LeScanCallbackImpl(Action<TransportAddress, BleAdScanResult> onDevice) : ScanCallback
    {
        private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

        public override void OnScanResult(ScanCallbackType callbackType, ScanResult? result)
        {
            if (result?.Device == null)
                return;
            if (!TryDeviceToAddress(result.Device, out var addr))
                return;
            var key = Convert.ToBase64String(addr.Data);
            if (!_seen.Add(key))
                return;
            var scanResult = ParseScanResult(result);
            onDevice(addr, scanResult);
        }

        private static BleAdScanResult ParseScanResult(ScanResult result)
        {
            var record = result.ScanRecord;
            if (record == null)
                return default;

            var parsed = default(BleAdScanResult);
            var mfg = record.ManufacturerSpecificData;
            if (mfg != null)
            {
                for (var i = 0; i < mfg.Size(); i++)
                {
                    var companyId = (ushort)mfg.KeyAt(i);
                    var bytes = mfg.ValueAt(i);
                    if (bytes == null)
                        continue;
                    parsed = BleAdvertisementIdentityParser.Merge(parsed,
                        BleAdvertisementIdentityParser.ParseManufacturerData(companyId, bytes));
                }
            }

            if (parsed.HasIdentity)
                return parsed;

            var serviceData = record.ServiceData;
            if (serviceData == null)
                return parsed;

            Span<byte> uuidBytes = stackalloc byte[16];
            if (!BleShortP2PGattProtocol.ServiceUuid.TryWriteBytes(uuidBytes))
                return parsed;

            var serviceAdvertised = record.ServiceUuids?.Contains(new ParcelUuid(ServiceUuidJava)) == true;
            var serviceUuidKey = new ParcelUuid(ServiceUuidJava);
            if (!serviceData.ContainsKey(serviceUuidKey))
                return parsed;

            var sectionPayload = serviceData.Get(serviceUuidKey);
            if (sectionPayload == null)
                return parsed;

            return BleAdvertisementIdentityParser.Merge(parsed,
                BleAdvertisementIdentityParser.ParseServiceDataSection(sectionPayload, uuidBytes, serviceAdvertised));
        }

        private static bool TryDeviceToAddress(BluetoothDevice device, out TransportAddress addr)
        {
            var s = device.Address?.Replace("-", ":", StringComparison.Ordinal);
            if (string.IsNullOrEmpty(s) || !BluetoothTransportAddress.TryParseMac(s, out var mac))
            {
                addr = new TransportAddress(TransportKind.Bluetooth, new byte[BluetoothTransportAddress.MacLength]);
                return false;
            }

            addr = BluetoothTransportAddress.FromMac(mac);
            return true;
        }
    }
}
