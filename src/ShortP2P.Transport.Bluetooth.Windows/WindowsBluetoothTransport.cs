using System.Collections.Concurrent;
using System.IO;
using System.Runtime.Versioning;
using System.Threading.Channels;
using ShortP2P.Transport;
using ShortP2P.Transport.Abstractions;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace ShortP2P.Transport.Bluetooth.Windows;

/// <summary>
///     Bluetooth Low Energy (GATT) на Windows через WinRT: исходящие записи в RX-характеристику пира
///     и входящие записи через локальный GATT-сервер.
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
public sealed class WindowsBluetoothTransport : ITransport
{
    private const int DefaultChannelCapacity = 256;
    private const uint BluetoothUnavailableHResult = 0x800710DF;

    private readonly WindowsBluetoothTransportOptions _options;
    private readonly Channel<TransportReceiveMessage> _inbound =
        Channel.CreateBounded<TransportReceiveMessage>(new BoundedChannelOptions(DefaultChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

    private readonly ConcurrentDictionary<ulong, GattCharacteristic> _bleOutboundRx = new();
    private readonly ConcurrentDictionary<ulong, BluetoothLEDevice> _blePeerDevices = new();
    private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _sendLocks = new();

    private CancellationTokenSource? _runCts;
    private GattServiceProvider? _bleServiceProvider;
    private GattLocalCharacteristic? _bleRxCharacteristic;

    public WindowsBluetoothTransport() : this(default)
    {
    }

    public WindowsBluetoothTransport(WindowsBluetoothTransportOptions options)
    {
        _options = options;
    }

    public TransportKind Kind => TransportKind.Bluetooth;

    public ChannelReader<TransportReceiveMessage> Inbound => _inbound.Reader;

    public bool IsRunning => _bleServiceProvider != null;

    public static bool IsUnavailableError(Exception ex)
    {
        for (var cur = ex; cur != null; cur = cur.InnerException)
        {
            if ((uint)cur.HResult == BluetoothUnavailableHResult)
                return true;
            if (cur is InvalidOperationException &&
                (cur.Message.Contains("Bluetooth LE device not found", StringComparison.OrdinalIgnoreCase) ||
                 cur.Message.Contains("Bluetooth LE device is not paired", StringComparison.OrdinalIgnoreCase) ||
                 cur.Message.Contains("Bluetooth LE pairing", StringComparison.OrdinalIgnoreCase) ||
                 cur.Message.Contains("Bluetooth LE PairAsync", StringComparison.OrdinalIgnoreCase) ||
                 cur.Message.Contains("automatic pairing is not available", StringComparison.OrdinalIgnoreCase) ||
                 cur.Message.Contains("BLE service not found", StringComparison.OrdinalIgnoreCase) ||
                 cur.Message.Contains("BLE RX characteristic not found", StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        return false;
    }

    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_bleServiceProvider != null) return;

        _runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ct = _runCts.Token;

        try
        {
            await StartBleGattAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when ((uint)ex.HResult == BluetoothUnavailableHResult)
        {
            // Bluetooth off / no adapter — GATT server not started.
        }

        if (_bleServiceProvider == null)
        {
            try
            {
                await _runCts.CancelAsync();
            }
            catch (ObjectDisposedException)
            {
                // ignore
            }

            _runCts.Dispose();
            _runCts = null;
        }
    }

    private async Task StartBleGattAsync(CancellationToken ct)
    {
        var create = await GattServiceProvider.CreateAsync(BleShortP2PGattProtocol.ServiceUuid).AsTask(ct)
            .ConfigureAwait(false);
        if (create.Error != BluetoothError.Success || create.ServiceProvider == null)
            return;
        _bleServiceProvider = create.ServiceProvider;

        var charParameters = new GattLocalCharacteristicParameters
        {
            CharacteristicProperties = GattCharacteristicProperties.Write
                                       | GattCharacteristicProperties.WriteWithoutResponse
                                       | GattCharacteristicProperties.Notify
                                       | GattCharacteristicProperties.Read,
            WriteProtectionLevel = GattProtectionLevel.Plain,
            UserDescription = "ShortP2P BLE TX/RX"
        };
        var charResult = await _bleServiceProvider.Service
            .CreateCharacteristicAsync(BleShortP2PGattProtocol.PeerRxCharacteristicUuid, charParameters)
            .AsTask(ct).ConfigureAwait(false);
        if (charResult.Error != BluetoothError.Success || charResult.Characteristic == null)
            return;
        _bleRxCharacteristic = charResult.Characteristic;
        _bleRxCharacteristic.WriteRequested += OnBleWriteRequested;
        StartBleGattProviderAdvertising(_bleServiceProvider, _options.GattDiscoverable);
    }

    private static void StartBleGattProviderAdvertising(GattServiceProvider provider, bool discoverable)
    {
        var advertising = new GattServiceProviderAdvertisingParameters
        {
            IsDiscoverable = true,
            IsConnectable = true,
        };

        provider.StartAdvertising(advertising);
    }

    private async void OnBleWriteRequested(GattLocalCharacteristic sender, GattWriteRequestedEventArgs args)
    {
        using var deferral = args.GetDeferral();
        try
        {
            var req = await args.GetRequestAsync();
            if (req == null)
                return;
            var reader = DataReader.FromBuffer(req.Value);
            var data = new byte[reader.UnconsumedBufferLength];
            reader.ReadBytes(data);
            if (data.Length == 0)
                return;

            var addr = await ResolveBleRemoteAddressAsync(args.Session).ConfigureAwait(false);
            await _inbound.Writer.WriteAsync(new TransportReceiveMessage(data, addr)).ConfigureAwait(false);
            req.Respond();
        }
        catch
        {
            // ignore malformed writes
        }
    }

    private static async Task<TransportAddress> ResolveBleRemoteAddressAsync(GattSession? session)
    {
        if (session != null)
        {
            try
            {
                var dev = await BluetoothLEDevice.FromIdAsync(session.DeviceId.Id);
                if (dev != null)
                    return new TransportAddress(TransportKind.Bluetooth,
                        BluetoothMacAddress.FromBluetoothAddress(dev.BluetoothAddress));
            }
            catch
            {
                // ignore
            }
        }

        return new TransportAddress(TransportKind.Bluetooth, new byte[BluetoothMacAddress.MacLength]);
    }

    public async ValueTask SendAsync(ReadOnlyMemory<byte> payload, TransportAddress destination,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (destination.Kind != TransportKind.Bluetooth)
            throw new ArgumentException("Destination must be Bluetooth transport.", nameof(destination));
        var data = destination.Data;
        if (data.Length != BluetoothMacAddress.MacLength)
            throw new ArgumentException($"Bluetooth address must be {BluetoothMacAddress.MacLength} bytes (MAC).",
                nameof(destination));

        var btAddr = BluetoothMacAddress.ToBluetoothAddress(data);
        var sendLock = _sendLocks.GetOrAdd(btAddr, _ => new SemaphoreSlim(1, 1));
        await sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await SendViaBleAsync(btAddr, payload, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            sendLock.Release();
        }
    }

    private async Task SendViaBleAsync(ulong bluetoothAddress, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        var rx = await GetOrConnectBleRxCharacteristicAsync(bluetoothAddress, ct).ConfigureAwait(false);
        var writer = new DataWriter();
        writer.WriteBytes(payload.ToArray());
        var status = await rx.WriteValueAsync(writer.DetachBuffer(), GattWriteOption.WriteWithoutResponse).AsTask(ct)
            .ConfigureAwait(false);
        if (status != GattCommunicationStatus.Success)
            throw new IOException("BLE write failed.");
    }

    private static async Task EnsureBlePairedAsync(BluetoothLEDevice device, CancellationToken ct)
    {
        if (device.DeviceInformation.Pairing.IsPaired)
            return;

        var pairing = device.DeviceInformation.Pairing;
        if (!pairing.CanPair)
        {
            device.Dispose();
            throw new InvalidOperationException(
                "Bluetooth LE device is not paired and automatic pairing is not available (CanPair is false).");
        }

        const DevicePairingKinds kinds =
            DevicePairingKinds.ConfirmOnly
            | DevicePairingKinds.DisplayPin
            | DevicePairingKinds.ProvidePin
            | DevicePairingKinds.ConfirmPinMatch;

        var custom = pairing.Custom;
        custom.PairingRequested += OnBleCustomPairingRequested;
        DevicePairingResult result;
        try
        {
            result = await custom.PairAsync(kinds, DevicePairingProtectionLevel.Default).AsTask(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            device.Dispose();
            throw new InvalidOperationException("Bluetooth LE PairAsync failed.", ex);
        }
        finally
        {
            custom.PairingRequested -= OnBleCustomPairingRequested;
        }

        if (result.Status != DevicePairingResultStatus.Paired)
        {
            device.Dispose();
            throw new InvalidOperationException(
                $"Bluetooth LE pairing did not complete: {result.Status}. Accept the pairing prompt on Windows or on the remote device.");
        }
    }

    private static void OnBleCustomPairingRequested(DeviceInformationCustomPairing sender,
        DevicePairingRequestedEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            switch (args.PairingKind)
            {
                case DevicePairingKinds.ConfirmOnly:
                case DevicePairingKinds.DisplayPin:
                case DevicePairingKinds.ConfirmPinMatch:
                    args.Accept();
                    break;
                case DevicePairingKinds.ProvidePin:
                    break;
            }
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async Task<GattCharacteristic> GetOrConnectBleRxCharacteristicAsync(ulong bluetoothAddress, CancellationToken ct)
    {
        if (_bleOutboundRx.TryGetValue(bluetoothAddress, out var cached))
            return cached;

        if (!_blePeerDevices.TryGetValue(bluetoothAddress, out var device))
        {
            device = await TryOpenBluetoothLeDeviceAsync(bluetoothAddress, ct).ConfigureAwait(false);
            if (device == null)
                throw new InvalidOperationException(
                    "Bluetooth LE device not found. Pair the device in Windows Settings, ensure Bluetooth is on, " +
                    "and that the MAC in the contact matches the peer (try re-adding after a fresh scan). " +
                    "FromBluetoothAddressAsync only works for paired devices or devices recently seen by the system.");

            await EnsureBlePairedAsync(device, ct).ConfigureAwait(false);
            _blePeerDevices[bluetoothAddress] = device;
        }

        var serviceResult = await device.GetGattServicesForUuidAsync(BleShortP2PGattProtocol.ServiceUuid).AsTask(ct)
            .ConfigureAwait(false);
        if (serviceResult.Status != GattCommunicationStatus.Success || serviceResult.Services.Count == 0)
            throw new InvalidOperationException("BLE service not found on remote device.");
        var service = serviceResult.Services[0];
        var charResult = await service.GetCharacteristicsForUuidAsync(BleShortP2PGattProtocol.PeerRxCharacteristicUuid)
            .AsTask(ct)
            .ConfigureAwait(false);
        var rx = charResult.Characteristics.FirstOrDefault()
                 ?? throw new InvalidOperationException("BLE RX characteristic not found on remote device.");
        _bleOutboundRx[bluetoothAddress] = rx;
        return rx;
    }

    /// <summary>
    ///     WinRT часто возвращает null из <see cref="BluetoothLEDevice.FromBluetoothAddressAsync"/>, если пир не в кэше
    ///     и не сопряжён. Пробуем несколько API и оба порядка байт MAC (ошибка при ручном вводе / чужой формат QR).
    /// </summary>
    private static async Task<BluetoothLEDevice?> TryOpenBluetoothLeDeviceAsync(ulong bluetoothAddress,
        CancellationToken ct)
    {
        foreach (var addr in AlternateBluetoothAddresses(bluetoothAddress))
        {
            var dev = await TryOpenBluetoothLeDeviceOneAddressAsync(addr, ct).ConfigureAwait(false);
            if (dev != null)
                return dev;
        }

        return null;
    }

    private static IEnumerable<ulong> AlternateBluetoothAddresses(ulong bluetoothAddress)
    {
        yield return bluetoothAddress;
        var mac = BluetoothMacAddress.FromBluetoothAddress(bluetoothAddress);
        var rev = new byte[BluetoothMacAddress.MacLength];
        for (var i = 0; i < mac.Length; i++) rev[i] = mac[^(i + 1)];
        var reversed = BluetoothMacAddress.ToBluetoothAddress(rev);
        if (reversed != bluetoothAddress)
            yield return reversed;
    }

    private static async Task<BluetoothLEDevice?> TryOpenBluetoothLeDeviceOneAddressAsync(ulong bluetoothAddress,
        CancellationToken ct)
    {
        var dev = await BluetoothLEDevice.FromBluetoothAddressAsync(bluetoothAddress).AsTask(ct).ConfigureAwait(false);
        if (dev != null)
            return dev;
        
        dev = await BluetoothLEDevice.FromBluetoothAddressAsync(bluetoothAddress, BluetoothAddressType.Public)
            .AsTask(ct).ConfigureAwait(false);
        if (dev != null)
            return dev;

        dev = await BluetoothLEDevice.FromBluetoothAddressAsync(bluetoothAddress, BluetoothAddressType.Random)
            .AsTask(ct).ConfigureAwait(false);
        if (dev != null)
            return dev;

        var selector = BluetoothLEDevice.GetDeviceSelectorFromBluetoothAddress(bluetoothAddress);
        var infos = await DeviceInformation.FindAllAsync(selector).AsTask(ct).ConfigureAwait(false);
        if (infos.Count == 0)
            return null;
        return await BluetoothLEDevice.FromIdAsync(infos[0].Id).AsTask(ct).ConfigureAwait(false);
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        if (_bleServiceProvider == null && _bleRxCharacteristic == null && _blePeerDevices.Count == 0 &&
            _bleOutboundRx.Count == 0 && _runCts == null)
            return;

        try
        {
            _runCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // ignore
        }

        _bleOutboundRx.Clear();

        foreach (var kv in _blePeerDevices)
            kv.Value.Dispose();

        _blePeerDevices.Clear();

        if (_bleRxCharacteristic != null)
        {
            _bleRxCharacteristic.WriteRequested -= OnBleWriteRequested;
            _bleRxCharacteristic = null;
        }

        if (_bleServiceProvider != null)
        {
            try
            {
                _bleServiceProvider.StopAdvertising();
            }
            catch
            {
                // ignore
            }

            _bleServiceProvider = null;
        }

        _runCts?.Dispose();
        _runCts = null;

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private bool _disposed;

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await StopAsync().ConfigureAwait(false);
        _inbound.Writer.TryComplete();
        foreach (var s in _sendLocks.Values) s.Dispose();
        _sendLocks.Clear();
    }
}
