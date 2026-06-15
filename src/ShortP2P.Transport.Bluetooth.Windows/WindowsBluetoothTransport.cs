using System.Collections.Concurrent;
using System.Runtime.Versioning;
using System.Threading.Channels;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;
using Microsoft.Extensions.Logging;
using ShortP2P.Auth.Data;
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
    private GattServiceProvider? _bleServiceProvider;
    private BluetoothLEAdvertisementWatcher? _advertisementWatcher;
    private bool _isStarted;
    private GattLocalCharacteristic? _bleRxCharacteristic;
    private readonly WindowsBluetoothTransportOptions _options;

    static WindowsBluetoothTransport()
    {
        Channel = System.Threading.Channels.Channel.CreateBounded<TransportReceiveMessage>(new BoundedChannelOptions(DefaultChannelCapacity)
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
    public WindowsBluetoothTransport(WindowsBluetoothTransportOptions options) => _options = options;

    private const int DefaultChannelCapacity = 1024;

    public ValueTask DisposeAsync()
    {
        throw new NotImplementedException();
    }

    public TransportKind Kind => TransportKind.Bluetooth;
    public ChannelReader<TransportReceiveMessage> Inbound => Channel.Reader;

    private static Channel<TransportReceiveMessage> Channel { get; }

    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        var create = await GattServiceProvider.CreateAsync(BleShortP2PGattProtocol.ServiceUuid).AsTask(cancellationToken)
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
        
        _isStarted = true;
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
            
            var addr = new TransportAddress(TransportKind.Bluetooth, BluetoothMacAddress.FromBluetoothAddress(device.BluetoothAddress));

            await Channel.Writer.WriteAsync(new TransportReceiveMessage(data, addr)).ConfigureAwait(false);
        }
        catch
        {
            // ignore malformed writes
        }
    }


    public ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _advertisementWatcher?.Stop();
            _bleServiceProvider?.StopAdvertising();
        
            _isStarted = false;
            return ValueTask.CompletedTask;
        }
        catch (Exception exception)
        {
            return ValueTask.FromException(exception);
        }
    }

    public async ValueTask SendAsync(ReadOnlyMemory<byte> payload, TransportAddress destination,
        CancellationToken cancellationToken = default)
    {
        if (destination.Kind != TransportKind.Bluetooth)
            throw new ArgumentException("Destination must be Bluetooth transport.", nameof(destination));
        var data = destination.Data;
        if (data.Length != BluetoothMacAddress.MacLength)
            throw new ArgumentException($"Bluetooth address must be {BluetoothMacAddress.MacLength} bytes (MAC).",
                nameof(destination)); 

        var device = await BluetoothLEDevice.FromBluetoothAddressAsync(BluetoothMacAddress.ToBluetoothAddress(data))
            .AsTask(cancellationToken).ConfigureAwait(false);
        if (device == null)
            return;
        
        var serviceResult = await device
            .GetGattServicesForUuidAsync(BleShortP2PGattProtocol.ServiceUuid, BluetoothCacheMode.Uncached)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);
        
        if (serviceResult.Status != GattCommunicationStatus.Success || serviceResult.Services.Count == 0)
            return;

        var service = serviceResult.Services[0];
        var rxResult = await service.GetCharacteristicsForUuidAsync(BleShortP2PGattProtocol.PeerRxCharacteristicUuid)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);
        var rx = rxResult.Characteristics.FirstOrDefault();
        if (rx == null)
            return;

        var writer = new DataWriter();
        writer.WriteBytes(payload.ToArray());
        var status = await rx.WriteValueAsync(writer.DetachBuffer(), GattWriteOption.WriteWithoutResponse).AsTask(cancellationToken)
            .ConfigureAwait(false);
        if (status != GattCommunicationStatus.Success)
            throw new IOException("BLE write failed.");
    }

    private void OnAdvertisementReceived(BluetoothLEAdvertisementWatcher sender,
        BluetoothLEAdvertisementReceivedEventArgs args)
    {
        var addr = args.BluetoothAddress;
        if (addr == 0)
            return;

        if (args.Advertisement.ServiceUuids.Contains(BleShortP2PGattProtocol.ServiceUuid))
        {
            var transportAddress =
                new TransportAddress(TransportKind.Bluetooth, BluetoothMacAddress.FromBluetoothAddress(addr));

            if (_options.LocalNetworkId is not { } localNetworkId || localNetworkId.IsEmpty)
                return;

            var networkIdPacket = BleNetworkIdPacketCodec.BuildPacket(localNetworkId);

            // Делимся своим networkId
            _ = SendAsync(networkIdPacket, transportAddress);
        }
    }
}