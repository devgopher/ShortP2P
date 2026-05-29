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

            var mfg = record.ManufacturerSpecificData;
            if (mfg != null && mfg.Size() > 0)
            {
                var entries = new List<BleManufacturerDataEntry>(mfg.Size());
                for (var i = 0; i < mfg.Size(); i++)
                {
                    var bytes = ReadManufacturerPayload(mfg.ValueAt(i));
                    if (bytes == null)
                        continue;
                    entries.Add(new BleManufacturerDataEntry((ushort)mfg.KeyAt(i), bytes));
                }

                var fromManufacturer = BleAdvertisementIdentityParser.ParseManufacturerEntries(entries);
                if (fromManufacturer.HasIdentity)
                    return fromManufacturer;
            }

            var serviceData = record.ServiceData;
            if (serviceData == null)
                return default;

            Span<byte> uuidBytes = stackalloc byte[16];
            if (!BleShortP2PGattProtocol.ServiceUuid.TryWriteBytes(uuidBytes))
                return default;

            var serviceAdvertised = record.ServiceUuids?.Contains(new ParcelUuid(ServiceUuidJava)) == true;
            var serviceUuidKey = new ParcelUuid(ServiceUuidJava);
            if (!serviceData.ContainsKey(serviceUuidKey))
                return default;

            if (!serviceData.TryGetValue(serviceUuidKey, out var sectionPayload) || sectionPayload == null)
                return default;

            return BleAdvertisementIdentityParser.ParseServiceDataSection(sectionPayload, uuidBytes,
                serviceAdvertised);
        }

        private static byte[]? ReadManufacturerPayload(object? value)
        {
            if (value is byte[] bytes)
                return bytes;
            if (value is Java.Nio.ByteBuffer buffer)
            {
                var arr = new byte[buffer.Remaining()];
                buffer.Get(arr);
                return arr;
            }

            return null;
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
