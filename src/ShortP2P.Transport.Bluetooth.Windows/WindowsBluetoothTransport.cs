using System.Collections.Concurrent;
using System.IO;
using System.Runtime.Versioning;
using System.Threading.Channels;
using ShortP2P.Transport.Abstractions;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace ShortP2P.Transport.Bluetooth.Windows;

/// <summary>
///     Классический Bluetooth (RFCOMM / Serial Port Profile) на Windows через WinRT.
///     Устройства должны быть сопряжены в системе; хост-приложению при необходимости нужны capabilities Bluetooth в манифесте.
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
public sealed class WindowsBluetoothTransport : ITransport
{
    private const int DefaultChannelCapacity = 256;

    private readonly Channel<TransportReceiveMessage> _inbound =
        Channel.CreateBounded<TransportReceiveMessage>(new BoundedChannelOptions(DefaultChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

    private readonly ConcurrentDictionary<ulong, StreamSocket> _outbound = new();
    private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _sendLocks = new();

    private RfcommServiceProvider? _rfcommProvider;
    private StreamSocketListener? _socketListener;
    private CancellationTokenSource? _runCts;

    public TransportKind Kind => TransportKind.Bluetooth;

    public ChannelReader<TransportReceiveMessage> Inbound => _inbound.Reader;

    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_rfcommProvider != null) return;

        _runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ct = _runCts.Token;

        RfcommServiceProvider provider;
        try
        {
            provider = await RfcommServiceProvider.CreateAsync(RfcommServiceId.SerialPort).AsTask(ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when ((uint)ex.HResult == 0x800710DF)
        {
            throw new InvalidOperationException(
                "Bluetooth is unavailable (radio off or no adapter). Turn Bluetooth on and retry.", ex);
        }

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
            var socket = await GetOrConnectOutboundAsync(btAddr, cancellationToken).ConfigureAwait(false);
            await using var stream = socket.OutputStream.AsStreamForWrite();
            await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
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

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        if (_rfcommProvider == null) return;

        try
        {
            _runCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        foreach (var kv in _outbound)
            kv.Value.Dispose();

        _outbound.Clear();

        try
        {
            _rfcommProvider.StopAdvertising();
        }
        catch
        {
            // ignore
        }

        _rfcommProvider = null;

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
