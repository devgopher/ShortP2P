using System.Collections.Concurrent;
using System.IO;
using System.Runtime.Versioning;
using System.Threading.Channels;
using ShortP2P.Transport.Abstractions;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using Microsoft.Extensions.Logging;

namespace ShortP2P.Transport.Bluetooth.Windows;

/// <summary>
///     Классический Bluetooth (RFCOMM / Serial Port Profile) на Windows через WinRT.
///     Устройства должны быть сопряжены в системе; хост-приложению при необходимости нужны capabilities Bluetooth в манифесте.
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
public sealed class WindowsBluetoothTransport : ITransport
{
    private const int DefaultChannelCapacity = 256;
    private const uint BluetoothUnavailableHResult = 0x800710DF;
    private static readonly Guid BleServiceUuid = Guid.Parse("9FE8E58B-AF85-4D91-B245-2B40EA0439C7");
    private static readonly Guid BleRxCharacteristicUuid = Guid.Parse("8DFE6F10-6CB7-4E73-A918-DC47AC34D9E9");

    private readonly Channel<TransportReceiveMessage> _inbound =
        Channel.CreateBounded<TransportReceiveMessage>(new BoundedChannelOptions(DefaultChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

    private readonly ConcurrentDictionary<ulong, StreamSocket> _outbound = new();
    private readonly ConcurrentDictionary<ulong, GattCharacteristic> _bleOutboundRx = new();
    private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _sendLocks = new();
    // private readonly ILogger<WindowsBluetoothTransport> _logger;

    private RfcommServiceProvider? _rfcommProvider;
    private StreamSocketListener? _socketListener;
    private CancellationTokenSource? _runCts;
    private GattServiceProvider? _bleServiceProvider;
    private GattLocalCharacteristic? _bleRxCharacteristic;

    public TransportKind Kind => TransportKind.Bluetooth;

    public ChannelReader<TransportReceiveMessage> Inbound => _inbound.Reader;

    public bool IsRunning => _rfcommProvider != null || _bleServiceProvider != null;

    public async Task<IReadOnlyList<TransportAddress>> GetPairedDeviceAddressesAsync(
        CancellationToken cancellationToken = default)
    {
        var selector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
        var infos = await DeviceInformation.FindAllAsync(selector).AsTask(cancellationToken).ConfigureAwait(false);
        var list = new List<TransportAddress>(infos.Count);
        foreach (var info in infos)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var dev = await BluetoothDevice.FromIdAsync(info.Id).AsTask(cancellationToken).ConfigureAwait(false);
                if (dev == null)
                    continue;
                list.Add(new TransportAddress(TransportKind.Bluetooth,
                    BluetoothMacAddress.FromBluetoothAddress(dev.BluetoothAddress)));
            }
            catch
            {
                // skip broken pair item
            }
        }

        return list;
    }

    public static bool IsUnavailableError(Exception ex)
    {
        for (var cur = ex; cur != null; cur = cur.InnerException)
        {
            if ((uint)cur.HResult == BluetoothUnavailableHResult)
                return true;
            if (cur is InvalidOperationException &&
                (cur.Message.Contains("Bluetooth device not found or not paired", StringComparison.OrdinalIgnoreCase) ||
                 cur.Message.Contains("service not found on the remote device", StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        return false;
    }

    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_rfcommProvider != null || _bleServiceProvider != null) return;

        _runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ct = _runCts.Token;

        RfcommServiceProvider? provider = null;
        try
        {
            provider = await RfcommServiceProvider.CreateAsync(RfcommServiceId.SerialPort).AsTask(ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when ((uint)ex.HResult == BluetoothUnavailableHResult)
        {
            //_logger.LogError("Bluetooth is unavailable (radio off or no adapter). Turn Bluetooth on and retry.");
        }

        if (provider != null)
        {
            _rfcommProvider = provider;
            var listener = new StreamSocketListener();
            _socketListener = listener;
            listener.ConnectionReceived += (s, args) => _ = OnConnectionReceivedAsync(args, ct);

            await listener.BindServiceNameAsync(
                    provider.ServiceId.AsString(),
                    SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication)
                .AsTask(ct)
                .ConfigureAwait(false);

            provider.StartAdvertising(listener, true);
        }

        await StartBleGattAsync(ct).ConfigureAwait(false);
    }

    private async Task StartBleGattAsync(CancellationToken ct)
    {
        var create = await GattServiceProvider.CreateAsync(BleServiceUuid).AsTask(ct).ConfigureAwait(false);
        if (create.Error != BluetoothError.Success || create.ServiceProvider == null)
            return;
        _bleServiceProvider = create.ServiceProvider;

        var charParameters = new GattLocalCharacteristicParameters
        {
            CharacteristicProperties = GattCharacteristicProperties.Write | GattCharacteristicProperties.WriteWithoutResponse,
            WriteProtectionLevel = GattProtectionLevel.Plain,
            UserDescription = "ShortP2P BLE RX",
        };
        var charResult = await _bleServiceProvider.Service.CreateCharacteristicAsync(BleRxCharacteristicUuid, charParameters)
            .AsTask(ct).ConfigureAwait(false);
        if (charResult.Error != BluetoothError.Success || charResult.Characteristic == null)
            return;
        _bleRxCharacteristic = charResult.Characteristic;
        _bleRxCharacteristic.WriteRequested += OnBleWriteRequested;
        StartBleGattProviderAdvertising(_bleServiceProvider);
    }

    /// <summary>
    ///     Включает периферийную рекламу BLE для зарегистрированного GATT-провайдера (обнаружение и приём подключений).
    /// </summary>
    private static void StartBleGattProviderAdvertising(GattServiceProvider provider)
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

    private async Task OnConnectionReceivedAsync(StreamSocketListenerConnectionReceivedEventArgs args,
        CancellationToken ct)
    {
        var socket = args.Socket;
        BluetoothDevice? remote;
        try
        {
            remote = await BluetoothDevice.FromHostNameAsync(socket.Information.RemoteHostName).AsTask(ct)
                .ConfigureAwait(false);
        }
        catch
        {
            socket.Dispose();
            return;
        }

        if (remote == null)
        {
            socket.Dispose();
            return;
        }

        var mac = BluetoothMacAddress.FromBluetoothAddress(remote.BluetoothAddress);
        var addr = new TransportAddress(TransportKind.Bluetooth, mac);
        await RunReceiveLoopAsync(socket, addr, ct).ConfigureAwait(false);
    }

    private async Task RunReceiveLoopAsync(StreamSocket socket, TransportAddress remoteAddress, CancellationToken ct)
    {
        try
        {
            var reader = new DataReader(socket.InputStream) { InputStreamOptions = InputStreamOptions.Partial };
            while (!ct.IsCancellationRequested)
            {
                var load = await reader.LoadAsync(65536).AsTask(ct).ConfigureAwait(false);
                if (load == 0) break;
                var buf = new byte[load];
                reader.ReadBytes(buf);
                var msg = new TransportReceiveMessage(buf, remoteAddress);
                await _inbound.Writer.WriteAsync(msg, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // ignore
        }
        catch
        {
            // Соединение закрыто или ошибка чтения — выходим из цикла.
        }
        finally
        {
            socket.Dispose();
        }
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
            try
            {
                var socket = await GetOrConnectOutboundAsync(btAddr, cancellationToken).ConfigureAwait(false);
                await using var stream = socket.OutputStream.AsStreamForWrite();
                await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                await SendViaBleAsync(btAddr, payload, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            sendLock.Release();
        }
    }

    private async Task<StreamSocket> GetOrConnectOutboundAsync(ulong bluetoothAddress, CancellationToken ct)
    {
        if (_outbound.TryGetValue(bluetoothAddress, out var existing))
        {
            try
            {
                _ = existing.Information;
                return existing;
            }
            catch
            {
                _outbound.TryRemove(bluetoothAddress, out _);
                existing.Dispose();
            }
        }

        var device = await BluetoothDevice.FromBluetoothAddressAsync(bluetoothAddress).AsTask(ct).ConfigureAwait(false);
        if (device == null) throw new InvalidOperationException("Bluetooth device not found or not paired.");

        var servicesResult =
            await device.GetRfcommServicesForIdAsync(RfcommServiceId.SerialPort).AsTask(ct).ConfigureAwait(false);
        if (servicesResult.Services.Count == 0)
            throw new InvalidOperationException(
                "Serial Port (RFCOMM) service not found on the remote device. Ensure SPP is available.");

        var service = servicesResult.Services[0];
        var socket = new StreamSocket();
        await socket.ConnectAsync(service.ConnectionHostName, service.ConnectionServiceName).AsTask(ct)
            .ConfigureAwait(false);

        _outbound[bluetoothAddress] = socket;
        return socket;
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

    private async Task<GattCharacteristic> GetOrConnectBleRxCharacteristicAsync(ulong bluetoothAddress, CancellationToken ct)
    {
        if (_bleOutboundRx.TryGetValue(bluetoothAddress, out var cached))
            return cached;

        var device = await BluetoothLEDevice.FromBluetoothAddressAsync(bluetoothAddress).AsTask(ct).ConfigureAwait(false);
        if (device == null)
            throw new InvalidOperationException("Bluetooth LE device not found or not paired.");

        var serviceResult = await device.GetGattServicesForUuidAsync(BleServiceUuid).AsTask(ct).ConfigureAwait(false);
        var service = serviceResult.Services.FirstOrDefault()
                      ?? throw new InvalidOperationException("BLE service not found on remote device.");
        var charResult = await service.GetCharacteristicsForUuidAsync(BleRxCharacteristicUuid).AsTask(ct)
            .ConfigureAwait(false);
        var rx = charResult.Characteristics.FirstOrDefault()
                 ?? throw new InvalidOperationException("BLE RX characteristic not found on remote device.");
        _bleOutboundRx[bluetoothAddress] = rx;
        return rx;
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        if (_rfcommProvider == null && _bleServiceProvider == null)
            return;

        try
        {
            _runCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // ignore
        }

        foreach (var kv in _outbound)
            kv.Value.Dispose();

        _outbound.Clear();
        _bleOutboundRx.Clear();

        if (_rfcommProvider != null)
        {
            try
            {
                _rfcommProvider.StopAdvertising();
            }
            catch
            {
                // ignore
            }

            _rfcommProvider = null;
        }
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

        if (_socketListener != null)
        {
            _socketListener.Dispose();
            _socketListener = null;
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
