using System.Collections.Concurrent;
using System.Threading.Channels;
using Android.Bluetooth;
using Android.Bluetooth.LE;
using Android.Content;
using Android.OS;
using Java.Util;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Transport.Bluetooth.Android;

/// <summary>
///     BLE GATT: локальный Server (RX приём / TX передача) + Central к пиру (write в RX, subscribe на TX).
/// </summary>
public sealed class AndroidBluetoothTransport : ITransport
{
    private const int DefaultChannelCapacity = 256;
    private const int MinApiForGattServerWrite = 31;

    private static readonly UUID ServiceUuidJava = UUID.FromString(BleShortP2PGattProtocol.ServiceUuid.ToString("D"));

    private static readonly UUID RxUuidJava =
        UUID.FromString(BleShortP2PGattProtocol.PeerRxCharacteristicUuid.ToString("D"));

    private static readonly UUID TxUuidJava =
        UUID.FromString(BleShortP2PGattProtocol.PeerTxCharacteristicUuid.ToString("D"));

    private readonly Context _context;

    private readonly Channel<TransportReceiveMessage> _inbound = Channel.CreateBounded<TransportReceiveMessage>(
        new BoundedChannelOptions(DefaultChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

    private readonly ConcurrentDictionary<string, byte> _networkIdOfferedToMac = new(StringComparer.OrdinalIgnoreCase);
    private readonly AndroidBluetoothTransportOptions _options;

    private readonly ConcurrentDictionary<string, OutboundPeerState> _outbound = new(StringComparer.OrdinalIgnoreCase);

    private readonly TaskCompletionSource<bool> _serviceAddedTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private BluetoothAdapter? _adapter;
    private AdvertiseCallbackImpl? _advertiseCallback;
    private BluetoothLeAdvertiser? _advertiser;
    private BluetoothManager? _btManager;
    private bool _disposed;
    private BluetoothGattServer? _gattServer;

    private CancellationTokenSource? _runCts;
    private volatile bool _started;

    public AndroidBluetoothTransport(Context context, AndroidBluetoothTransportOptions options = default)
    {
        _context = context.ApplicationContext ?? context;
        _options = options;
    }

    public ChannelReader<TransportReceiveMessage> Inbound => _inbound.Reader;

    public TransportKind Kind => TransportKind.Bluetooth;

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

            var rx = new BluetoothGattCharacteristic(RxUuidJava,
                GattProperty.Write | GattProperty.WriteNoResponse | GattProperty.Read,
                GattPermission.Read | GattPermission.Write);
            service.AddCharacteristic(rx);

            var tx = new BluetoothGattCharacteristic(TxUuidJava,
                GattProperty.Notify | GattProperty.Read,
                GattPermission.Read);
            service.AddCharacteristic(tx);

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
                var scanRspBuilder = new AdvertiseData.Builder()!;

                if (_options.GattDiscoverable)
                    scanRspBuilder.SetIncludeDeviceName(true);
                var scanRsp = scanRspBuilder.Build();
                _advertiseCallback = new AdvertiseCallbackImpl();
                _advertiser.StartAdvertising(settings, data, scanRsp, _advertiseCallback);
            }

            _started = true;
            startedOk = true;
            _ = PushNetworkIdToBondedPeersAsync(ct);
        }
        finally
        {
            if (!startedOk)
            {
                try
                {
                    await _runCts?.CancelAsync();
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
            await SendLockedAsync(payload, data, state, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            state.Gate.Release();
        }
    }

    private async Task SendLockedAsync(ReadOnlyMemory<byte> payload, byte[] mac6, OutboundPeerState state,
        CancellationToken cancellationToken)
    {
        var device = ResolveRemoteDevice(mac6);
        if (state.Gatt == null || state.PeerRx == null)
        {
            state.DiscoveryTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cb = new OutboundGattCallback(this, state);
            var gatt = device.ConnectGatt(_context, false, cb, BluetoothTransports.Le);
            if (gatt == null)
                throw new IOException("ConnectGatt returned null.");
            state.Gatt = gatt;
            using var reg = cancellationToken.Register(() => state.DiscoveryTcs?.TrySetCanceled(cancellationToken));
            var discovered = await state.DiscoveryTcs.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken)
                .ConfigureAwait(false);
            if (!discovered || state.PeerRx == null)
                throw new IOException("BLE service/RX characteristic not found on peer.");
        }

        var bytes = payload.ToArray();
        if (!state.PeerRx.SetValue(bytes))
            throw new IOException("Failed to set characteristic value.");
        state.PeerRx.WriteType = GattWriteType.NoResponse;
        if (!state.Gatt!.WriteCharacteristic(state.PeerRx))
            throw new IOException("BLE write to peer RX failed.");
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

    internal void OnInboundFromPeer(BluetoothDevice device, byte[] value)
    {
        if (value.Length == 0)
            return;
        if (!DeviceToTransportAddress(device, out var addr))
            addr = new TransportAddress(TransportKind.Bluetooth, new byte[BluetoothTransportAddress.MacLength]);

        if (BleShortP2PGattProtocol.TryParseNetworkIdAnnouncePacket(value, out var peerNetworkId))
        {
            if (_options.LocalNetworkId is { } local && local == peerNetworkId)
                return;
            try
            {
                _options.OnPeerNetworkIdReceived?.Invoke(addr, peerNetworkId);
            }
            catch
            {
                // ignore subscriber errors
            }
        }

        _ = _inbound.Writer.TryWrite(new TransportReceiveMessage(value, addr));
    }

    private async Task PushNetworkIdToBondedPeersAsync(CancellationToken ct)
    {
        foreach (var addr in GetBondedDeviceAddresses())
            await TryOfferNetworkIdToPeerAsync(addr, ct).ConfigureAwait(false);
    }

    private IReadOnlyList<TransportAddress> GetBondedDeviceAddresses()
    {
        var list = new List<TransportAddress>();
        var bonded = _adapter?.BondedDevices;
        if (bonded == null)
            return list;
        foreach (var device in bonded)
            if (DeviceToTransportAddress(device, out var addr))
                list.Add(addr);

        return list;
    }

    public Task<IReadOnlyList<TransportAddress>> GetPairedDeviceAddressesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<TransportAddress>>(GetBondedDeviceAddresses());
    }

    private async Task TryOfferNetworkIdToPeerAsync(TransportAddress destination, CancellationToken ct)
    {
        if (_options.LocalNetworkId is not { } localNetworkId || localNetworkId.IsEmpty)
            return;
        var macKey = BluetoothTransportAddress.ToMacString(destination.Data);
        if (!_networkIdOfferedToMac.TryAdd(macKey, 0))
            return;

        try
        {
            var packet = BleShortP2PGattProtocol.BuildNetworkIdAnnouncePacket(localNetworkId);
            await SendAsync(packet, destination, ct).ConfigureAwait(false);
        }
        catch
        {
            _networkIdOfferedToMac.TryRemove(macKey, out _);
        }
    }

    internal void OnServiceAdded(bool success)
    {
        _serviceAddedTcs.TrySetResult(success);
    }

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
            await _runCts?.CancelAsync();
        }
        catch
        {
            // ignore
        }

        if (_advertiser != null && _advertiseCallback != null)
            try
            {
                _advertiser.StopAdvertising(_advertiseCallback);
            }
            catch
            {
                // ignore
            }

        _advertiseCallback = null;
        _networkIdOfferedToMac.Clear();

        foreach (var kv in _outbound.ToArray())
        {
            try
            {
                if (kv.Value.PeerTx != null && kv.Value.Gatt != null)
                    kv.Value.Gatt.SetCharacteristicNotification(kv.Value.PeerTx, false);
            }
            catch
            {
                // ignore
            }

            try
            {
                kv.Value.Gatt?.Close();
            }
            catch
            {
                // ignore
            }

            kv.Value.Gatt = null;
            kv.Value.PeerRx = null;
            kv.Value.PeerTx = null;
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
        public TaskCompletionSource<bool>? DiscoveryTcs;
        public BluetoothGatt? Gatt;
        public BluetoothGattCharacteristic? PeerRx;
        public BluetoothGattCharacteristic? PeerTx;
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

        public ShortP2PGattServerCallback(AndroidBluetoothTransport owner)
        {
            _owner = owner;
        }

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
                          && characteristic.Uuid.Equals(RxUuidJava)
                          && value != null
                          && value.Length > 0;
            if (matches)
                _owner.OnInboundFromPeer(device, value);

            if (!responseNeeded)
                return;

            var st = matches ? GattStatus.Success : GattStatus.Failure;
            _owner._gattServer.SendResponse(device, requestId, st, offset, matches ? value : null);
        }
    }

    private sealed class OutboundGattCallback : BluetoothGattCallback
    {
        private readonly AndroidBluetoothTransport _owner;
        private readonly OutboundPeerState _state;

        public OutboundGattCallback(AndroidBluetoothTransport owner, OutboundPeerState state)
        {
            _owner = owner;
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
                    _state.PeerRx = null;
                    _state.PeerTx = null;
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

            var rx = svc.GetCharacteristic(RxUuidJava);
            if (rx == null)
            {
                _state.DiscoveryTcs?.TrySetResult(false);
                return;
            }

            _state.PeerRx = rx;
            _state.PeerTx = svc.GetCharacteristic(TxUuidJava);
            if (_state.PeerTx != null)
            {
                gatt.SetCharacteristicNotification(_state.PeerTx, true);
                var cccd = _state.PeerTx.GetDescriptor(
                    UUID.FromString("00002902-0000-1000-8000-00805f9b34fb"));
                if (cccd != null)
                {
                    cccd.SetValue(BluetoothGattDescriptor.EnableNotificationValue.ToArray());
                    gatt.WriteDescriptor(cccd);
                }
            }

            _state.DiscoveryTcs?.TrySetResult(true);
        }

        public override void OnCharacteristicChanged(BluetoothGatt? gatt, BluetoothGattCharacteristic? characteristic)
        {
            if (gatt == null || characteristic == null || !characteristic.Uuid.Equals(TxUuidJava))
                return;
            var value = characteristic.GetValue();
            if (value == null || value.Length == 0)
                return;
            var device = gatt.Device;
            if (device == null)
                return;
            _owner.OnInboundFromPeer(device, value);
        }
    }
}