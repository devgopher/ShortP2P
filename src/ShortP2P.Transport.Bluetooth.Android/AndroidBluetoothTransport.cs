using System.Collections.Concurrent;
using System.IO;
using System.Threading.Channels;
using Android.Bluetooth;
using Android.Bluetooth.LE;
using Android.Content;
using Android.OS;
using Java.Util;
using ShortP2P.Transport;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Transport.Bluetooth.Android;

/// <summary>
///     BLE GATT-транспорт: локальный GATT Server (peripheral) + исходящие записи как GATT Client (central).
///     Протокол совпадает с <c>WindowsBluetoothTransport</c> (см. <see cref="BleShortP2PGattProtocol" />).
/// </summary>
/// <remarks>
///     Приём записей на GATT Server использует <c>OnCharacteristicWriteRequest</c> (Android 12 / API 31+).
/// </remarks>
public sealed class AndroidBluetoothTransport : ITransport
{
    private const int DefaultChannelCapacity = 256;
    private const int MinApiForGattServerWrite = 31;

    private static readonly UUID ServiceUuidJava = UUID.FromString(BleShortP2PGattProtocol.ServiceUuid.ToString("D"));
    private static readonly UUID CharUuidJava = UUID.FromString(BleShortP2PGattProtocol.PeerRxCharacteristicUuid.ToString("D"));

    private readonly Context _context;
    private readonly AndroidBluetoothTransportOptions _options;
    private readonly Channel<TransportReceiveMessage> _inbound = Channel.CreateBounded<TransportReceiveMessage>(
        new BoundedChannelOptions(DefaultChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

    private readonly ConcurrentDictionary<string, OutboundPeerState> _outbound = new(StringComparer.OrdinalIgnoreCase);
    private BluetoothManager? _btManager;
    private BluetoothAdapter? _adapter;
    private BluetoothLeAdvertiser? _advertiser;
    private BluetoothGattServer? _gattServer;
    private AdvertiseCallbackImpl? _advertiseCallback;
    private readonly TaskCompletionSource<bool> _serviceAddedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private CancellationTokenSource? _runCts;
    private volatile bool _started;
    private bool _disposed;

    public AndroidBluetoothTransport(Context context, AndroidBluetoothTransportOptions options = default)
    {
        _context = context.ApplicationContext ?? context;
        _options = options;
    }

    public TransportKind Kind => TransportKind.Bluetooth;

    public ChannelReader<TransportReceiveMessage> Inbound => _inbound.Reader;

    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
            return;

        if ((int)Build.VERSION.SdkInt < MinApiForGattServerWrite)
        {
            global::Android.Util.Log.Warn("ShortP2P-BLE",
                $"Bluetooth LE transport needs API {MinApiForGattServerWrite}+ (Android 12) for GATT server writes; transport not started.");
            return;
        }

        _runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ct = _runCts.Token;
        var startedOk = false;
        try
        {
            _btManager = (BluetoothManager)_context.GetSystemService(Context.BluetoothService)!;
            _adapter = _btManager.Adapter;
            if (_adapter == null || !_adapter.IsEnabled)
                return;

            var serverCallback = new ShortP2PGattServerCallback(this);
            _gattServer = _btManager.OpenGattServer(_context, serverCallback);
            if (_gattServer == null)
                return;

            var service = new BluetoothGattService(ServiceUuidJava, GattServiceType.Primary);
            var ch = new BluetoothGattCharacteristic(CharUuidJava,
                GattProperty.Write | GattProperty.WriteNoResponse | GattProperty.Notify | GattProperty.Read,
                GattPermission.Read | GattPermission.Write);
            service.AddCharacteristic(ch);
            if (!_gattServer.AddService(service))
            {
                try
                {
                    _gattServer.Close();
                }
                catch
                {
                    // ignore
                }

                _gattServer = null;
                return;
            }

            var ok = await _serviceAddedTcs.Task.WaitAsync(ct).ConfigureAwait(false);
            if (!ok)
            {
                try
                {
                    _gattServer.Close();
                }
                catch
                {
                    // ignore
                }

                _gattServer = null;
                return;
            }

            _advertiser = _adapter.BluetoothLeAdvertiser;
            if (_advertiser != null)
            {
                var settings = new AdvertiseSettings.Builder()!
                    .SetAdvertiseMode(AdvertiseMode.Balanced)!
                    .SetTxPowerLevel((AdvertiseTx)(int)AdvertiseTxPower.Medium)!
                    .SetConnectable(true)!
                    .Build();
                var data = new AdvertiseData.Builder()!
                    .AddServiceUuid(new ParcelUuid(ServiceUuidJava))!
                    .Build();
                var scanRsp = _options.GattDiscoverable
                    ? new AdvertiseData.Builder()!.SetIncludeDeviceName(true)!.Build()
                    : new AdvertiseData.Builder()!.Build();
                _advertiseCallback = new AdvertiseCallbackImpl();
                _advertiser.StartAdvertising(settings, data, scanRsp, _advertiseCallback);
            }

            _started = true;
            startedOk = true;
        }
        finally
        {
            if (!startedOk)
            {
                try
                {
                    _runCts?.Cancel();
                }
                catch
                {
                    // ignore
                }

                _runCts?.Dispose();
                _runCts = null;
            }
        }
    }

    public async ValueTask SendAsync(ReadOnlyMemory<byte> payload, TransportAddress destination,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (destination.Kind != TransportKind.Bluetooth)
            throw new ArgumentException("Destination must be Bluetooth transport.", nameof(destination));
        var data = destination.Data;
        if (data.Length != BluetoothTransportAddress.MacLength)
            throw new ArgumentException($"Bluetooth address must be {BluetoothTransportAddress.MacLength} bytes (MAC).",
                nameof(destination));

        if (_adapter == null || !_adapter.IsEnabled)
            throw new IOException("Bluetooth adapter is not available.");

        var macKey = BluetoothTransportAddress.ToMacString(data);
        var state = _outbound.GetOrAdd(macKey, _ => new OutboundPeerState());
        await state.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await SendLockedAsync(payload, data, macKey, state, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            state.Gate.Release();
        }
    }

    private async Task SendLockedAsync(ReadOnlyMemory<byte> payload, byte[] mac6, string macKey, OutboundPeerState state,
        CancellationToken cancellationToken)
    {
        var device = ResolveRemoteDevice(mac6);
        if (state.Gatt == null || state.Rx == null)
        {
            state.DiscoveryTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cb = new OutboundGattCallback(this, state, macKey);
            var gatt = device.ConnectGatt(_context, false, cb, BluetoothTransports.Le);
            if (gatt == null)
                throw new IOException("ConnectGatt returned null.");
            state.Gatt = gatt;
            using var reg = cancellationToken.Register(() => state.DiscoveryTcs?.TrySetCanceled(cancellationToken));
            var discovered = await state.DiscoveryTcs.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken)
                .ConfigureAwait(false);
            if (!discovered || state.Rx == null)
                throw new IOException("BLE service/characteristic not found on peer.");
        }

        var bytes = payload.ToArray();
        if (!state.Rx.SetValue(bytes))
            throw new IOException("Failed to set characteristic value.");
        state.Rx.WriteType = GattWriteType.NoResponse;
        if (!state.Gatt!.WriteCharacteristic(state.Rx))
            throw new IOException("BLE write failed.");
    }

    private BluetoothDevice ResolveRemoteDevice(byte[] mac6)
    {
        var d1 = _adapter!.GetRemoteDevice(mac6);
        if (d1 != null)
            return d1;
        var rev = new byte[BluetoothTransportAddress.MacLength];
        for (var i = 0; i < mac6.Length; i++) rev[i] = mac6[^(i + 1)];
        return _adapter.GetRemoteDevice(rev)
               ?? throw new IOException("GetRemoteDevice failed for MAC.");
    }

    internal void OnInboundWrite(BluetoothDevice device, byte[] value)
    {
        if (value.Length == 0)
            return;
        if (!DeviceToTransportAddress(device, out var addr))
            addr = new TransportAddress(TransportKind.Bluetooth, new byte[BluetoothTransportAddress.MacLength]);
        _ = _inbound.Writer.TryWrite(new TransportReceiveMessage(value, addr));
    }

    internal void OnServiceAdded(bool success) => _serviceAddedTcs.TrySetResult(success);

    private static bool DeviceToTransportAddress(BluetoothDevice device, out TransportAddress addr)
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

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        _started = false;
        try
        {
            _runCts?.Cancel();
        }
        catch
        {
            // ignore
        }

        if (_advertiser != null && _advertiseCallback != null)
        {
            try
            {
                _advertiser.StopAdvertising(_advertiseCallback);
            }
            catch
            {
                // ignore
            }
        }

        _advertiseCallback = null;

        foreach (var kv in _outbound.ToArray())
        {
            try
            {
                kv.Value.Gatt?.Close();
            }
            catch
            {
                // ignore
            }

            kv.Value.Gatt = null;
            kv.Value.Rx = null;
            try
            {
                kv.Value.Gate.Dispose();
            }
            catch
            {
                // ignore
            }

            _outbound.TryRemove(kv.Key, out _);
        }

        if (_gattServer != null)
        {
            try
            {
                _gattServer.Close();
            }
            catch
            {
                // ignore
            }

            _gattServer = null;
        }

        _adapter = null;
        _btManager = null;
        _runCts?.Dispose();
        _runCts = null;
        await Task.CompletedTask.ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        await StopAsync().ConfigureAwait(false);
        _inbound.Writer.TryComplete();
    }

    private sealed class OutboundPeerState
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        public BluetoothGatt? Gatt;
        public BluetoothGattCharacteristic? Rx;
        public TaskCompletionSource<bool>? DiscoveryTcs;
    }

    private sealed class AdvertiseCallbackImpl : AdvertiseCallback
    {
        public override void OnStartFailure(AdvertiseFailure errorCode)
        {
            global::Android.Util.Log.Warn("ShortP2P-BLE", $"BLE advertise start failure: {errorCode}");
        }
    }

    private sealed class ShortP2PGattServerCallback : BluetoothGattServerCallback
    {
        private readonly AndroidBluetoothTransport _owner;

        public ShortP2PGattServerCallback(AndroidBluetoothTransport owner) => _owner = owner;

        public override void OnServiceAdded(GattStatus status, BluetoothGattService? service)
        {
            _owner.OnServiceAdded(status == GattStatus.Success);
        }

        public override void OnCharacteristicWriteRequest(BluetoothDevice? device, int requestId,
            BluetoothGattCharacteristic? characteristic, bool preparedWrite, bool responseNeeded, int offset,
            byte[]? value)
        {
            if (device == null || _owner._gattServer == null)
                return;

            var matches = !preparedWrite
                          && offset == 0
                          && characteristic != null
                          && characteristic.Uuid.Equals(CharUuidJava)
                          && value != null
                          && value.Length > 0;
            if (matches)
                _owner.OnInboundWrite(device, value);

            if (!responseNeeded)
                return;

            var st = matches ? GattStatus.Success : GattStatus.Failure;
            _owner._gattServer.SendResponse(device, requestId, st, offset, matches ? value : null);
        }
    }

    private sealed class OutboundGattCallback : BluetoothGattCallback
    {
        private readonly OutboundPeerState _state;

        public OutboundGattCallback(AndroidBluetoothTransport owner, OutboundPeerState state, string macKey)
        {
            _ = owner;
            _ = macKey;
            _state = state;
        }

        public override void OnConnectionStateChange(BluetoothGatt? gatt, GattStatus status, ProfileState newState)
        {
            if (gatt == null)
                return;
            if (newState != ProfileState.Connected)
            {
                if (ReferenceEquals(_state.Gatt, gatt))
                {
                    _state.Rx = null;
                    try
                    {
                        gatt.Close();
                    }
                    catch
                    {
                        // ignore
                    }

                    if (ReferenceEquals(_state.Gatt, gatt))
                        _state.Gatt = null;
                }

                _state.DiscoveryTcs?.TrySetResult(false);
                return;
            }

            gatt.RequestMtu(517);
            if (!gatt.DiscoverServices())
                _state.DiscoveryTcs?.TrySetResult(false);
        }

        public override void OnMtuChanged(BluetoothGatt? gatt, int mtu, GattStatus status)
        {
        }

        public override void OnServicesDiscovered(BluetoothGatt? gatt, GattStatus status)
        {
            if (gatt == null || status != GattStatus.Success)
            {
                _state.DiscoveryTcs?.TrySetResult(false);
                return;
            }

            var svc = gatt.GetService(ServiceUuidJava);
            if (svc == null)
            {
                _state.DiscoveryTcs?.TrySetResult(false);
                return;
            }

            var ch = svc.GetCharacteristic(CharUuidJava);
            if (ch == null)
            {
                _state.DiscoveryTcs?.TrySetResult(false);
                return;
            }

            _state.Rx = ch;
            _state.DiscoveryTcs?.TrySetResult(true);
        }
    }
}
