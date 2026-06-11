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
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Transport.Bluetooth.Windows;

/// <summary>
///     Bluetooth Low Energy (GATT) на Windows через WinRT: исходящие записи в RX-характеристику пира
///     и входящие записи через локальный GATT-сервер.
/// </summary>
[SupportedOSPlatform("windows10.0.18362.0")]
public sealed class WindowsBluetoothTransport(WindowsBluetoothTransportOptions options) : ITransport
{
    private GattServiceProvider? _bleServiceProvider;
    private BluetoothLEAdvertisementWatcher? _advertisementWatcher;
    private bool _isStarted;
    
    public ValueTask DisposeAsync()
    {
        throw new NotImplementedException();
    }

    public TransportKind Kind => TransportKind.Bluetooth;
    public ChannelReader<TransportReceiveMessage> Inbound { get; }
    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        var create = await GattServiceProvider.CreateAsync(BleShortP2PGattProtocol.ServiceUuid).AsTask(cancellationToken)
            .ConfigureAwait(false);
        if (create.Error != BluetoothError.Success || create.ServiceProvider == null)
            return;
        _bleServiceProvider = create.ServiceProvider;

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

    public ValueTask SendAsync(ReadOnlyMemory<byte> payload, TransportAddress destination,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
    
    private void OnAdvertisementReceived(BluetoothLEAdvertisementWatcher sender,
        BluetoothLEAdvertisementReceivedEventArgs args)
    {
        var addr = args.BluetoothAddress;
        if (addr == 0)
            return;

        // Не блокируем поток watcher'а: складываем рекламу в очередь и разбираем её в фоновом потоке.
        // _bleAdvertisementQueue.Writer.TryWrite(args);
    }
}