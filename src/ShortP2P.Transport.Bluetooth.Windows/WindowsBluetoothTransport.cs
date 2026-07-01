using System.Collections.Concurrent;
using System.IO;
using System.Runtime.Versioning;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;
using ShortP2P.Transport;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Transport.Bluetooth.Windows;

/// <summary>
///     Bluetooth Low Energy (GATT) на Windows через WinRT: исходящие записи в RX-характеристику пира
///     и входящие записи через локальный GATT-сервер.
/// </summary>
[SupportedOSPlatform("windows10.0.18362.0")]
public sealed class WindowsBluetoothTransport : ITransport
{
    private const int DefaultChannelCapacity = 1024;
    private const int OutboundQueueCapacity = 1024;
    private static readonly TimeSpan SendThrottleInterval = TimeSpan.FromMilliseconds(100);

    private readonly Dictionary<ulong, DateTime> _lastNetworkIdSending = new(100);
    private readonly ConcurrentDictionary<ulong, GattSession> _peerServerSessions = new();
    private readonly ConcurrentDictionary<ulong, GattCharacteristic> _centralPeerTxCharacteristics = new();
    private readonly TimeSpan _networkIdPeriod = new(0, 0, 30);
    private readonly WindowsBluetoothTransportOptions _options;
    private BluetoothLEAdvertisementWatcher? _advertisementWatcher;
    private GattLocalCharacteristic? _bleRxCharacteristic;
    private GattLocalCharacteristic? _bleTxCharacteristic;
    private GattServiceProvider? _bleServiceProvider;
    private bool _isStarted;
    private Channel<OutboundSendRequest>? _outbound;
    private CancellationTokenSource? _runCts;
    private Task? _sendLoopTask;

    static WindowsBluetoothTransport()
    {
        Channel = System.Threading.Channels.Channel.CreateBounded<TransportReceiveMessage>(
            new BoundedChannelOptions(DefaultChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            });
    }

    /// <summary>
    ///     Bluetooth Low Energy (GATT) на Windows через WinRT: исходящие записи в RX-характеристику пира
    ///     и входящие записи через локальный GATT-сервер.
    /// </summary>
    public WindowsBluetoothTransport(WindowsBluetoothTransportOptions options)
    {
        _options = options;
    }

    private static Channel<TransportReceiveMessage> Channel { get; }

    public ValueTask DisposeAsync()
    {
        throw new NotImplementedException();
    }

    public TransportKind Kind => TransportKind.Bluetooth;
    public ChannelReader<TransportReceiveMessage> Inbound => Channel.Reader;

    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isStarted)
            return;

        var create = await GattServiceProvider.CreateAsync(BleShortP2PGattProtocol.ServiceUuid)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);
        if (create.Error != BluetoothError.Success || create.ServiceProvider == null)
            return;

        var rxParameters = new GattLocalCharacteristicParameters
        {
            CharacteristicProperties = GattCharacteristicProperties.Write
                                       | GattCharacteristicProperties.WriteWithoutResponse
                                       | GattCharacteristicProperties.Notify
                                       | GattCharacteristicProperties.Broadcast,
            WriteProtectionLevel = GattProtectionLevel.Plain,
            ReadProtectionLevel = GattProtectionLevel.Plain,
            UserDescription = "ShortP2P BLE RX"
        };

        _bleServiceProvider = create.ServiceProvider;

        var rxResult = await _bleServiceProvider.Service
            .CreateCharacteristicAsync(BleShortP2PGattProtocol.PeerRxCharacteristicUuid, rxParameters)
            .AsTask(cancellationToken).ConfigureAwait(false);

        if (rxResult.Error != BluetoothError.Success || rxResult.Characteristic == null)
            return;
        _bleRxCharacteristic = rxResult.Characteristic;
        _bleRxCharacteristic.WriteRequested += OnBleRxWriteRequested;

        var txParameters = new GattLocalCharacteristicParameters
        {
            CharacteristicProperties = GattCharacteristicProperties.Notify | GattCharacteristicProperties.Read,
            WriteProtectionLevel = GattProtectionLevel.Plain,
            ReadProtectionLevel = GattProtectionLevel.Plain,
            UserDescription = "ShortP2P BLE TX"
        };

        var txResult = await _bleServiceProvider.Service
            .CreateCharacteristicAsync(BleShortP2PGattProtocol.PeerTxCharacteristicUuid, txParameters)
            .AsTask(cancellationToken).ConfigureAwait(false);
        if (txResult.Error != BluetoothError.Success || txResult.Characteristic == null)
            return;
        _bleTxCharacteristic = txResult.Characteristic;


        var advertising = new GattServiceProviderAdvertisingParameters
        {
            IsDiscoverable = true,
            IsConnectable = true
        };

        _bleServiceProvider.StartAdvertising(advertising);

        _advertisementWatcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active
        };
        _advertisementWatcher.Received += OnAdvertisementReceived;
        _advertisementWatcher.Start();

        _runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _outbound = System.Threading.Channels.Channel.CreateBounded<OutboundSendRequest>(
            new BoundedChannelOptions(OutboundQueueCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });

        _isStarted = true;
        _sendLoopTask = ProcessOutboundLoopAsync(_runCts.Token);
    }


    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_isStarted)
            return;

        _isStarted = false;

        try
        {
            _advertisementWatcher?.Stop();
            _bleServiceProvider?.StopAdvertising();

            if (_runCts != null)
            {
                await _runCts.CancelAsync().ConfigureAwait(false);
                _outbound?.Writer.TryComplete();

                if (_sendLoopTask != null)
                    try
                    {
                        await _sendLoopTask.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // expected on stop
                    }

                while (_outbound?.Reader.TryRead(out var pending) == true)
                    pending.Completion.TrySetCanceled(cancellationToken);
            }
        }
        finally
        {
            _runCts?.Dispose();
            _runCts = null;
            _sendLoopTask = null;
            _outbound = null;
        }
    }

    public async ValueTask SendAsync(ReadOnlyMemory<byte> payload, TransportAddress destination,
        CancellationToken cancellationToken = default)
    {
        if (destination.Kind != TransportKind.Bluetooth)
            throw new ArgumentException("Destination must be Bluetooth transport.", nameof(destination));
        var destinationData = destination.Data;
        if (destinationData.Length != BluetoothMacAddress.MacLength)
            throw new ArgumentException($"Bluetooth address must be {BluetoothMacAddress.MacLength} bytes (MAC).",
                nameof(destination));
        if (!_isStarted || _outbound == null)
            throw new InvalidOperationException("Bluetooth transport is not started.");

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = new OutboundSendRequest(payload.ToArray(), destinationData.ToArray(), tcs);

        await using var registration = cancellationToken.Register(static state =>
        {
            var (pending, token) = ((OutboundSendRequest, CancellationToken))state!;
            pending.Completion.TrySetCanceled(token);
        }, (request, cancellationToken));

        await _outbound.Writer.WriteAsync(request, cancellationToken).ConfigureAwait(false);
        await tcs.Task.ConfigureAwait(false);
    }

    private async void OnBleRxWriteRequested(GattLocalCharacteristic sender, GattWriteRequestedEventArgs args)
    {
        try
        {
            using var deferral = args.GetDeferral();
            var req = await args.GetRequestAsync();
            if (req == null)
                return;
            var reader = DataReader.FromBuffer(req.Value);
            var data = new byte[reader.UnconsumedBufferLength];
            reader.ReadBytes(data);
            if (data.Length == 0)
                return;
            if (req.Option == GattWriteOption.WriteWithResponse)
                req.Respond();

            var device = await BluetoothLEDevice.FromIdAsync(args.Session.DeviceId.Id);

            if (device == null)
                return;

            var addr = new TransportAddress(TransportKind.Bluetooth,
                BluetoothMacAddress.FromBluetoothAddress(device.BluetoothAddress));

            _peerServerSessions[device.BluetoothAddress] = args.Session;

            TransportTrafficLog.LogReceive(_options.Logger, addr, LocalBluetoothEndpoint(), data);

            await Channel.Writer.WriteAsync(new TransportReceiveMessage(data, addr)).ConfigureAwait(false);
        }
        catch
        {
            // ignore malformed writes
        }
    }

    private async Task ProcessOutboundLoopAsync(CancellationToken cancellationToken)
    {
        if (_outbound == null)
            return;

        try
        {
            await foreach (var request in _outbound.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    var sent = await TrySendOnceAsync(request.Payload, request.DestinationAddress, cancellationToken)
                        .ConfigureAwait(false);
                    if (!sent)
                        throw new IOException("BLE send failed: peer not reachable via GATT server notify or central.");
                    request.Completion.TrySetResult();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    request.Completion.TrySetCanceled(cancellationToken);
                    return;
                }
                catch (Exception ex)
                {
                    request.Completion.TrySetException(ex);
                }

                try
                {
                    await Task.Delay(SendThrottleInterval, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // expected on stop
        }
    }

    private async ValueTask<bool> TrySendOnceAsync(byte[] payload, byte[] mac,
        CancellationToken cancellationToken)
    {
        if (await TrySendViaServerNotifyAsync(payload, mac, cancellationToken).ConfigureAwait(false))
            return true;

        if (!ShouldUseCentralConnection(mac))
            return false;

        return await TrySendViaCentralAsync(payload, mac, cancellationToken).ConfigureAwait(false);
    }

    private bool ShouldUseCentralConnection(byte[] peerMac)
    {
        if (!TryGetLocalMacBytes(out var local))
            return true;
        return BluetoothTransportAddress.ShouldInitiateBleConnection(local, peerMac);
    }

    private bool TryGetLocalMacBytes(out byte[] mac)
    {
        mac = [];
        if (_options.LocalAdapterBluetoothAddress is not ulong addr || addr == 0)
            return false;
        mac = BluetoothMacAddress.FromBluetoothAddress(addr);
        return mac.Length == BluetoothTransportAddress.MacLength;
    }

    private async ValueTask<bool> TrySendViaServerNotifyAsync(byte[] payload, byte[] mac,
        CancellationToken cancellationToken)
    {
        var tx = _bleTxCharacteristic;
        if (tx == null)
            return false;

        var btAddr = BluetoothMacAddress.ToBluetoothAddress(mac);
        if (!_peerServerSessions.TryGetValue(btAddr, out var session))
            return false;

        var client = tx.SubscribedClients.FirstOrDefault(c =>
            string.Equals(c.Session?.DeviceId?.Id, session.DeviceId?.Id, StringComparison.OrdinalIgnoreCase));
        if (client == null)
            return false;

        var destination = new TransportAddress(TransportKind.Bluetooth, mac);
        TransportTrafficLog.LogSend(_options.Logger, LocalBluetoothEndpoint(), destination, payload);

        var writer = new DataWriter();
        writer.WriteBytes(payload);
        var notifyResult = await tx.NotifyValueAsync(writer.DetachBuffer(), client).AsTask(cancellationToken)
            .ConfigureAwait(false);
        return notifyResult.Status == GattCommunicationStatus.Success;
    }

    private async ValueTask<bool> TrySendViaCentralAsync(byte[] payload, byte[] mac,
        CancellationToken cancellationToken)
    {
        var destination = new TransportAddress(TransportKind.Bluetooth, mac);
        TransportTrafficLog.LogSend(_options.Logger, LocalBluetoothEndpoint(), destination, payload);

        var device = await BluetoothLEDevice.FromBluetoothAddressAsync(BluetoothMacAddress.ToBluetoothAddress(mac))
            .AsTask(cancellationToken).ConfigureAwait(false);
        if (device == null)
            return false;

        var serviceResult = await device
            .GetGattServicesForUuidAsync(BleShortP2PGattProtocol.ServiceUuid, BluetoothCacheMode.Uncached)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);

        if (serviceResult.Status != GattCommunicationStatus.Success || serviceResult.Services.Count == 0)
            return false;

        var service = serviceResult.Services[0];
        var rxResult = await service
            .GetCharacteristicsForUuidAsync(BleShortP2PGattProtocol.PeerRxCharacteristicUuid)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);

        var rx = rxResult.Characteristics.FirstOrDefault();
        if (rx == null)
            return false;

        await EnsureCentralPeerTxSubscriptionAsync(device.BluetoothAddress, service, cancellationToken)
            .ConfigureAwait(false);

        var writer = new DataWriter();
        writer.WriteBytes(payload);
        var status = await rx.WriteValueAsync(writer.DetachBuffer(), GattWriteOption.WriteWithoutResponse)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);
        return status == GattCommunicationStatus.Success;
    }

    private async Task EnsureCentralPeerTxSubscriptionAsync(ulong peerBluetoothAddress, GattDeviceService service,
        CancellationToken cancellationToken)
    {
        if (_centralPeerTxCharacteristics.ContainsKey(peerBluetoothAddress))
            return;

        var txResult = await service
            .GetCharacteristicsForUuidAsync(BleShortP2PGattProtocol.PeerTxCharacteristicUuid)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);
        var tx = txResult.Characteristics.FirstOrDefault();
        if (tx == null)
            return;

        var status = await tx.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);
        if (status != GattCommunicationStatus.Success)
            return;

        tx.ValueChanged += OnCentralPeerTxValueChanged;
        if (_centralPeerTxCharacteristics.TryAdd(peerBluetoothAddress, tx))
            return;

        tx.ValueChanged -= OnCentralPeerTxValueChanged;
    }

    private void OnCentralPeerTxValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        try
        {
            var peerAddr = sender.Service?.Device?.BluetoothAddress ?? 0;
            if (peerAddr == 0)
                return;

            var reader = DataReader.FromBuffer(args.CharacteristicValue);
            var data = new byte[reader.UnconsumedBufferLength];
            if (data.Length == 0)
                return;
            reader.ReadBytes(data);

            var addr = new TransportAddress(TransportKind.Bluetooth,
                BluetoothMacAddress.FromBluetoothAddress(peerAddr));
            TransportTrafficLog.LogReceive(_options.Logger, addr, LocalBluetoothEndpoint(), data);
            _ = Channel.Writer.WriteAsync(new TransportReceiveMessage(data, addr));
        }
        catch
        {
            // ignore malformed notify
        }
    }


    private void OnAdvertisementReceived(BluetoothLEAdvertisementWatcher sender,
        BluetoothLEAdvertisementReceivedEventArgs args)
    {
        if (_options.LocalNetworkId is not { } localNetworkId || localNetworkId.IsEmpty)
            return;

        var addr = args.BluetoothAddress;
        if (addr == 0)
            return;

        if (_lastNetworkIdSending.ContainsKey(addr) &&
            DateTime.UtcNow - _lastNetworkIdSending[addr] <= _networkIdPeriod)
            return;

        if (!args.Advertisement.ServiceUuids.Contains(BleShortP2PGattProtocol.ServiceUuid))
            return;

        var transportAddress =
            new TransportAddress(TransportKind.Bluetooth, BluetoothMacAddress.FromBluetoothAddress(addr));

        var networkIdPacket = BleNetworkIdPacketCodec.BuildPacket(localNetworkId);

        // Делимся своим networkId
        _ = SendAsync(networkIdPacket, transportAddress);

        _lastNetworkIdSending[addr] = DateTime.UtcNow;
    }

    private string LocalBluetoothEndpoint()
    {
        if (_options.LocalAdapterBluetoothAddress is ulong adapterAddr && adapterAddr != 0)
        {
            try
            {
                return BluetoothTransportAddress.ToMacString(
                    BluetoothMacAddress.FromBluetoothAddress(adapterAddr));
            }
            catch
            {
                // ignore
            }
        }

        return "BLE:local";
    }

    private sealed class OutboundSendRequest(byte[] payload, byte[] destinationAddress, TaskCompletionSource completion)
    {
        public byte[] Payload { get; } = payload;
        public byte[] DestinationAddress { get; } = destinationAddress;
        public TaskCompletionSource Completion { get; } = completion;
    }
}