using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.Versioning;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using ShortP2P.Auth.Data;
using ShortP2P.Transport;
using ShortP2P.Transport.Abstractions;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Storage.Streams;
using Buffer = Windows.Storage.Streams.Buffer;

namespace ShortP2P.Transport.Bluetooth.Windows;

/// <summary>
///     Bluetooth Low Energy (GATT) на Windows через WinRT: исходящие записи в RX-характеристику пира
///     и входящие записи через локальный GATT-сервер.
/// </summary>
[SupportedOSPlatform("windows10.0.18362.0")]
public sealed class WindowsBluetoothTransport(WindowsBluetoothTransportOptions options) : ITransport
{
    private const int DefaultChannelCapacity = 256;
    private const uint BluetoothUnavailableHResult = 0x800710DF;

    private readonly WindowsBluetoothTransportOptions _options = options;
    private readonly ILogger? _logger = options.Logger;
    private readonly BleAdvertisementMergeCache _bleAdvertisementMergeCache = new(options.Logger);

    private readonly Channel<TransportReceiveMessage> _inbound =
        Channel.CreateBounded<TransportReceiveMessage>(new BoundedChannelOptions(DefaultChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

    private static readonly StringComparer MacKeyComparer = StringComparer.OrdinalIgnoreCase;

    private readonly ConcurrentDictionary<string, GattCharacteristic> _bleOutboundPeerRx = new(MacKeyComparer);
    private readonly ConcurrentDictionary<string, GattCharacteristic> _bleOutboundPeerTx = new(MacKeyComparer);
    private readonly ConcurrentDictionary<string, BluetoothLEDevice> _blePeerDevices = new(MacKeyComparer);
    private readonly ConcurrentDictionary<string, BluetoothLEDevice> _bleAdvertisementDeviceCache = new(MacKeyComparer);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _sendLocks = new(MacKeyComparer);
    private TypedEventHandler<BluetoothLEAdvertisementPublisher, BluetoothLEAdvertisementPublisherStatusChangedEventArgs>?
        _manufacturerPublisherStatusHandler;

    private CancellationTokenSource? _runCts;
    private GattServiceProvider? _bleServiceProvider;
    private GattLocalCharacteristic? _bleRxCharacteristic;
    private GattLocalCharacteristic? _bleTxCharacteristic;
    private BluetoothLEAdvertisementWatcher? _bleShortP2PAdvertisementWatcher;
    private BluetoothLEAdvertisementPublisher? _networkIdManufacturerPublisher;
    private volatile string? _myBluetoothMac;
    private ulong _myBluetoothAddr;
    
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
                 cur.Message.Contains("BLE RX characteristic not found", StringComparison.OrdinalIgnoreCase) ||
                 cur.Message.Contains("BLE TX characteristic not found", StringComparison.OrdinalIgnoreCase)))
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

        var rxParameters = new GattLocalCharacteristicParameters
        {
            CharacteristicProperties = GattCharacteristicProperties.Write
                                       | GattCharacteristicProperties.WriteWithoutResponse
                                       | GattCharacteristicProperties.Read
                                       | GattCharacteristicProperties.Broadcast,
            WriteProtectionLevel = GattProtectionLevel.Plain,
            UserDescription = "ShortP2P BLE RX"
        };
        var rxResult = await _bleServiceProvider.Service
            .CreateCharacteristicAsync(BleShortP2PGattProtocol.PeerRxCharacteristicUuid, rxParameters)
            .AsTask(ct).ConfigureAwait(false);
        if (rxResult.Error != BluetoothError.Success || rxResult.Characteristic == null)
            return;
        _bleRxCharacteristic = rxResult.Characteristic;
        _bleRxCharacteristic.WriteRequested += OnBleRxWriteRequested;

        var txParameters = new GattLocalCharacteristicParameters
        {
            CharacteristicProperties = GattCharacteristicProperties.Notify | GattCharacteristicProperties.Read,
            WriteProtectionLevel = GattProtectionLevel.Plain,
            UserDescription = "ShortP2P BLE TX"
        };
        var txResult = await _bleServiceProvider.Service
            .CreateCharacteristicAsync(BleShortP2PGattProtocol.PeerTxCharacteristicUuid, txParameters)
            .AsTask(ct).ConfigureAwait(false);
        if (txResult.Error != BluetoothError.Success || txResult.Characteristic == null)
            return;
        _bleTxCharacteristic = txResult.Characteristic;
        if (options.LocalAdapterBluetoothAddress is { } preferred && preferred != 0)
            _myBluetoothMac = BluetoothTransportAddress.ToMacString(
                BluetoothMacAddress.FromBluetoothAddress(preferred));
        else
            _myBluetoothMac = await LocalAdapterBluetoothMac.TryGetAdapterMacStringAsync().ConfigureAwait(false)
                               ?? "none";

        _myBluetoothAddr = await LocalAdapterBluetoothMac.TryGetAdapterAddressAsync().ConfigureAwait(false)
                           ?? 0;
        
        StartBleGattProviderAdvertising(_bleServiceProvider, _options.GattDiscoverable, _options.LocalNetworkId, _logger); 
        //StartNetworkIdManufacturerAdvertising(_options.LocalNetworkId);
        StartBleShortP2PAdvertisementWatcher();
    }

    /// <summary>
    ///     WinRT часто не открывает устройство только по MAC, пока стек не «видел» рекламу.
    ///     Пассивный watcher по UUID ShortP2P держит кэш <see cref="BluetoothLEDevice" /> для последующих
    ///     <see cref="BluetoothLEDevice.FromBluetoothAddressAsync" /> без сопряжения в настройках.
    /// </summary>
    private void StartBleShortP2PAdvertisementWatcher()
    {
        try
        {
            var watcher = new BluetoothLEAdvertisementWatcher
            {
                ScanningMode = BluetoothLEScanningMode.Active
            };
            watcher.Received += OnBleShortP2PAdvertisementReceived;
            watcher.Start();
            _bleShortP2PAdvertisementWatcher = watcher;
            _logger?.LogInformation("BLE advertisement watcher started (active scan, no service filter)");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "BLE advertisement watcher failed to start");
        }
    }

    private async void OnBleShortP2PAdvertisementReceived(BluetoothLEAdvertisementWatcher sender,
        BluetoothLEAdvertisementReceivedEventArgs args)
    {
        var addr = args.BluetoothAddress;
        if (addr == 0)
            return;
        if (!BleWindowsAdvertisementHelper.IsShortP2P(args.Advertisement))
            return;
        var mac = BluetoothMacAddress.FromBluetoothAddress(addr);
        var macKey = MacCacheKey(mac);
        if (_blePeerDevices.ContainsKey(macKey))
            return;
        try
        {
            var dev = await BluetoothLEDevice.FromBluetoothAddressAsync(addr).AsTask().ConfigureAwait(false);

            if (dev == null)
                return;
            var scanResult = _bleAdvertisementMergeCache.Observe(addr, args.Advertisement);
            BleWindowsAdvertisementLog.LogAdvertisementReceived(_logger, addr, macKey, args, scanResult);
            if (_bleAdvertisementDeviceCache.TryAdd(macKey, dev))
                _logger?.LogInformation("BLE device cached from advertisement: {Mac}", macKey);
        }
        catch
        {
            // ignore
        }
    }

    private void StopBleShortP2PAdvertisementWatcher()
    {
        var w = _bleShortP2PAdvertisementWatcher;
        _bleShortP2PAdvertisementWatcher = null;
        if (w == null)
            return;
        try
        {
            w.Received -= OnBleShortP2PAdvertisementReceived;
            w.Stop();
        }
        catch
        {
            // ignore
        }
    }

    private static void StartBleGattProviderAdvertising(GattServiceProvider provider, bool discoverable,
        CompressedNetworkId? localNetworkId, ILogger? logger)
    {
        var advertising = new GattServiceProviderAdvertisingParameters
        {
            IsDiscoverable = discoverable,
            IsConnectable = true,
        };
        if (localNetworkId is { IsEmpty: false } networkId)
        {
            var payload = BleShortP2PGattProtocol.BuildGattServiceDataNetworkId(networkId);
            var writer = new DataWriter();
            writer.WriteBytes(payload);
            advertising.ServiceData = writer.DetachBuffer();
            BleWindowsAdvertisementLog.LogGattAdvertisingStarted(logger, discoverable, networkId, payload.Length);
        }
        else
            BleWindowsAdvertisementLog.LogGattAdvertisingStarted(logger, discoverable, null, null);

        provider.StartAdvertising(advertising);
    }

    /// <summary>
    ///     Manufacturer Data в scan response — надёжный канал полного NetworkId (17 байт) на Win11 при GATT-рекламе.
    /// </summary>
    private void StartNetworkIdManufacturerAdvertising(CompressedNetworkId? localNetworkId)
    {
        StopNetworkIdManufacturerAdvertising();
        if (localNetworkId is not { } networkId || networkId.IsEmpty)
            return;

        try
        {
            //var payload = BleShortP2PGattProtocol.BuildManufacturerNetworkIdPayload(networkId);
            var payload = new byte[CompressedNetworkId.WireLength];
            if (!localNetworkId.Value.TryWriteBytes(payload))
                return;
            
            var writer = new DataWriter();
            writer.WriteBytes(payload);
            var advertisement = new BluetoothLEAdvertisement();
            advertisement.ManufacturerData.Add(new BluetoothLEManufacturerData(
                BleShortP2PGattProtocol.ManufacturerCompanyId, writer.DetachBuffer()));
            advertisement.ServiceUuids.Add(BleShortP2PGattProtocol.ServiceUuid);
            
            advertisement.DataSections.Add(new BluetoothLEAdvertisementDataSection
            {
                //Data = writer.DetachBuffer(),
                DataType = BleAdvertisementIdentityParser.AdTypeServiceData128
            });
            
            _networkIdManufacturerPublisher = new BluetoothLEAdvertisementPublisher(advertisement);
            _manufacturerPublisherStatusHandler = (_, e) =>
                BleWindowsAdvertisementLog.LogPublisherStatus(_logger, e.Status);
            _networkIdManufacturerPublisher.StatusChanged += _manufacturerPublisherStatusHandler;
            _networkIdManufacturerPublisher.Start();
            BleWindowsAdvertisementLog.LogManufacturerPublisherStarted(_logger, networkId, payload.Length);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "BLE manufacturer publisher failed to start");
            StopNetworkIdManufacturerAdvertising();
        }
    }

    private void StopNetworkIdManufacturerAdvertising()
    {
        var publisher = _networkIdManufacturerPublisher;
        _networkIdManufacturerPublisher = null;
        if (publisher == null)
            return;
        try
        {
            if (_manufacturerPublisherStatusHandler != null)
                publisher.StatusChanged -= _manufacturerPublisherStatusHandler;
            _manufacturerPublisherStatusHandler = null;
            publisher.Stop();
            _logger?.LogInformation("BLE manufacturer publisher stopped");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "BLE manufacturer publisher stop failed");
            // ignore
        }
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
            {
                req.Respond();
            }
           
            var addr = await ResolveBleRemoteAddressAsync(args.Session).ConfigureAwait(false);
            await _inbound.Writer.WriteAsync(new TransportReceiveMessage(data, addr)).ConfigureAwait(false);
        }
        catch
        {
            // ignore malformed writes
        }
    }

    private static async Task<TransportAddress> ResolveBleRemoteAddressAsync(GattSession? session)
    {
        if (session == null)
            return new TransportAddress(TransportKind.Bluetooth, new byte[BluetoothMacAddress.MacLength]);
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

        var macKey = MacCacheKey(data);
        // var sendLock = _sendLocks.GetOrAdd(macKey, _ => new SemaphoreSlim(1, 1));
        // await sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await SendViaBleAsync(macKey, data, payload, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // ignore
        }
        finally
        {
            //sendLock.Release();
        }
    }

    private async Task SendViaBleAsync(string macKey, byte[] mac6, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        var rx = await GetOrConnectBleRxCharacteristicAsync(macKey, mac6, ct).ConfigureAwait(false);
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

    private async Task<GattCharacteristic> GetOrConnectBleRxCharacteristicAsync(string macKey, byte[] mac6,
        CancellationToken ct)
    {
        if (_bleOutboundPeerRx.TryGetValue(macKey, out var cached))
            return cached;

        if (!_blePeerDevices.TryGetValue(macKey, out var device))
        {
            device = await TryOpenBluetoothLeDeviceAsync(mac6, ct).ConfigureAwait(false);
            if (device == null)
                throw new InvalidOperationException(
                    "Bluetooth LE device not found. Leave ShortP2P running so advertising peers are seen, run a LAN/BLE scan, " +
                    "or pair in Windows Settings. Windows often cannot open BLE by MAC until the device was seen over the air.");

            try
            {
                var (rx, _) = await DiscoverBlePeerCharacteristicsWithOptionalPairingAsync(macKey, device, ct)
                    .ConfigureAwait(false);
                _blePeerDevices[macKey] = device;
                _bleOutboundPeerRx[macKey] = rx;
                return rx;
            }
            catch
            {
                try
                {
                    device.Dispose();
                }
                catch
                {
                    // ignore
                }

                throw;
            }
        }

        var (rxExisting, _) = await DiscoverBlePeerCharacteristicsWithOptionalPairingAsync(macKey, device, ct)
            .ConfigureAwait(false);
        _bleOutboundPeerRx[macKey] = rxExisting;
        return rxExisting;
    }

    private async Task<(GattCharacteristic rx, GattCharacteristic? tx)> DiscoverBlePeerCharacteristicsWithOptionalPairingAsync(
        string macKey, BluetoothLEDevice device, CancellationToken ct)
    {
        var first = await TryDiscoverBlePeerCharacteristicsAsync(device, ct).ConfigureAwait(false);
        if (first.rx != null)
        {
            if (first.tx != null)
                await EnsurePeerTxNotifySubscribedAsync(macKey, first.tx, ct).ConfigureAwait(false);
            return (first.rx, first.tx);
        }

        if (!device.DeviceInformation.Pairing.IsPaired &&
            (first.status == GattCommunicationStatus.AccessDenied ||
             first.status == GattCommunicationStatus.ProtocolError))
        {
            await EnsureBlePairedAsync(device, ct).ConfigureAwait(false);
            var second = await TryDiscoverBlePeerCharacteristicsAsync(device, ct).ConfigureAwait(false);
            if (second.rx != null)
            {
                if (second.tx != null)
                    await EnsurePeerTxNotifySubscribedAsync(macKey, second.tx, ct).ConfigureAwait(false);
                return (second.rx, second.tx);
            }
        }

        throw new InvalidOperationException("BLE service or RX characteristic not found on remote device.");
    }

    private static async Task<(GattCharacteristic? rx, GattCharacteristic? tx, GattCommunicationStatus status)>
        TryDiscoverBlePeerCharacteristicsAsync(BluetoothLEDevice device, CancellationToken ct)
    {
        var serviceResult = await device.GetGattServicesForUuidAsync(BleShortP2PGattProtocol.ServiceUuid).AsTask(ct)
            .ConfigureAwait(false);
        if (serviceResult.Status != GattCommunicationStatus.Success || serviceResult.Services.Count == 0)
            return (null, null, serviceResult.Status);

        var service = serviceResult.Services[0];
        var rxResult = await service.GetCharacteristicsForUuidAsync(BleShortP2PGattProtocol.PeerRxCharacteristicUuid)
            .AsTask(ct)
            .ConfigureAwait(false);
        var rx = rxResult.Characteristics.FirstOrDefault();
        if (rx == null)
            return (null, null, GattCommunicationStatus.ProtocolError);

        GattCharacteristic? tx = null;
        var txResult = await service.GetCharacteristicsForUuidAsync(BleShortP2PGattProtocol.PeerTxCharacteristicUuid)
            .AsTask(ct)
            .ConfigureAwait(false);
        tx = txResult.Characteristics.FirstOrDefault();

        return (rx, tx, GattCommunicationStatus.Success);
    }

    private async Task EnsurePeerTxNotifySubscribedAsync(string macKey, GattCharacteristic peerTx,
        CancellationToken ct)
    {
        if (_bleOutboundPeerTx.ContainsKey(macKey))
            return;

        peerTx.ValueChanged -= OnPeerTxValueChanged;
        peerTx.ValueChanged += OnPeerTxValueChanged;

        var cccd = await peerTx.WriteClientCharacteristicConfigurationDescriptorAsync(
            GattClientCharacteristicConfigurationDescriptorValue.Notify).AsTask(ct).ConfigureAwait(false);
        if (cccd != GattCommunicationStatus.Success)
            throw new InvalidOperationException("BLE TX notify subscription failed.");

        _bleOutboundPeerTx[macKey] = peerTx;
    }

    private async void OnPeerTxValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        try
        {
            var reader = DataReader.FromBuffer(args.CharacteristicValue);
            var data = new byte[reader.UnconsumedBufferLength];
            reader.ReadBytes(data);
            if (data.Length == 0)
                return;

            var addr = new TransportAddress(TransportKind.Bluetooth,
                BluetoothMacAddress.FromBluetoothAddress(sender.Service.Device.BluetoothAddress));
            await _inbound.Writer.WriteAsync(new TransportReceiveMessage(data, addr)).ConfigureAwait(false);
        }
        catch
        {
            // ignore malformed notifications
        }
    }

    /// <summary>
    ///     WinRT часто возвращает null из <see cref="BluetoothLEDevice.FromBluetoothAddressAsync"/>, если пир не в кэше
    ///     и не сопряжён. Пробуем несколько API и оба порядка байт MAC (ошибка при ручном вводе / чужой формат QR).
    /// </summary>
    private async Task<BluetoothLEDevice?> TryOpenBluetoothLeDeviceAsync(byte[] mac6, CancellationToken ct)
    {
        var macKey = MacCacheKey(mac6);
        if (_bleAdvertisementDeviceCache.TryGetValue(macKey, out var cached))
            return cached;

        foreach (var addr in AlternateBluetoothAddresses(mac6))
        {
            var dev = await TryOpenBluetoothLeDeviceOneAddressAsync(addr, macKey, ct).ConfigureAwait(false);
            if (dev != null)
                return dev;
        }

        return null;
    }

    private static string MacCacheKey(ReadOnlySpan<byte> mac6) => BluetoothTransportAddress.ToMacString(mac6);

    private static ulong[] AlternateBluetoothAddresses(byte[] mac6)
    {
        if (mac6.Length != BluetoothMacAddress.MacLength)
            return [];

        var primary = BluetoothMacAddress.ToBluetoothAddress(mac6);
        var rev = new byte[BluetoothMacAddress.MacLength];
        for (var i = 0; i < mac6.Length; i++) rev[i] = mac6[^(i + 1)];
        var reversed = BluetoothMacAddress.ToBluetoothAddress(rev);
        return reversed != primary ? [primary, reversed] : [primary];
    }

    private async Task<BluetoothLEDevice?> TryOpenBluetoothLeDeviceOneAddressAsync(ulong bluetoothAddress, string macKey,
        CancellationToken ct)
    {
        if (_bleAdvertisementDeviceCache.TryGetValue(macKey, out var warmed))
            return warmed;

        var dev = await BluetoothLEDevice.FromBluetoothAddressAsync(bluetoothAddress).AsTask(ct).ConfigureAwait(false);
        if (dev != null)
        {
            _bleAdvertisementDeviceCache.TryAdd(macKey, dev);
            return dev;
        }

        dev = await BluetoothLEDevice.FromBluetoothAddressAsync(bluetoothAddress, BluetoothAddressType.Public)
            .AsTask(ct).ConfigureAwait(false);
        if (dev != null)
        {
            _bleAdvertisementDeviceCache.TryAdd(macKey, dev);
            return dev;
        }

        dev = await BluetoothLEDevice.FromBluetoothAddressAsync(bluetoothAddress, BluetoothAddressType.Random)
            .AsTask(ct).ConfigureAwait(false);
        if (dev != null)
        {
            _bleAdvertisementDeviceCache.TryAdd(macKey, dev);
            return dev;
        }

        var selector = BluetoothLEDevice.GetDeviceSelectorFromBluetoothAddress(bluetoothAddress);
        var infos = await DeviceInformation.FindAllAsync(selector).AsTask(ct).ConfigureAwait(false);
        if (infos.Count == 0)
            return null;
        dev = await BluetoothLEDevice.FromIdAsync(infos[0].Id).AsTask(ct).ConfigureAwait(false);
        if (dev != null)
            _bleAdvertisementDeviceCache.TryAdd(macKey, dev);
        return dev;
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        if (_bleServiceProvider == null && _bleRxCharacteristic == null && _bleTxCharacteristic == null &&
            _blePeerDevices.Count == 0 &&
            _bleOutboundPeerRx.Count == 0 && _bleOutboundPeerTx.Count == 0 && _runCts == null &&
            _bleShortP2PAdvertisementWatcher == null &&
            _networkIdManufacturerPublisher == null &&
            _bleAdvertisementDeviceCache.IsEmpty)
            return;

        if (_runCts != null)
        {
            try
            {
                await _runCts.CancelAsync();
            }
            catch (ObjectDisposedException)
            {
                // ignore
            }
        }

        StopBleShortP2PAdvertisementWatcher();
        StopNetworkIdManufacturerAdvertising();

        foreach (var kv in _bleOutboundPeerTx)
        {
            try
            {
                kv.Value.ValueChanged -= OnPeerTxValueChanged;
            }
            catch
            {
                // ignore
            }
        }

        _bleOutboundPeerRx.Clear();
        _bleOutboundPeerTx.Clear();

        foreach (var kv in _blePeerDevices)
            kv.Value.Dispose();

        _blePeerDevices.Clear();

        foreach (var kv in _bleAdvertisementDeviceCache)
        {
            try
            {
                kv.Value.Dispose();
            }
            catch
            {
                // ignore
            }
        }

        _bleAdvertisementDeviceCache.Clear();

        if (_bleRxCharacteristic != null)
        {
            _bleRxCharacteristic.WriteRequested -= OnBleRxWriteRequested;
            _bleRxCharacteristic = null;
        }

        _bleTxCharacteristic = null;

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
