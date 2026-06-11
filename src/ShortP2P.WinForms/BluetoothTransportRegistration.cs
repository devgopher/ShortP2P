using Microsoft.Extensions.Logging;
using ShortP2P.Auth.Data;
using ShortP2P.Client.Bluetooth;
using ShortP2P.Client.Routing;
using ShortP2P.Discovery.Ble;
using ShortP2P.Transport.Abstractions;
using ShortP2P.Transport.Bluetooth.Windows;

namespace ShortP2P.WinForms;

internal sealed class BluetoothTransportRegistration : IAsyncDisposable, IBluetoothTransportProvider
{
    private readonly IBleDiscoveredPeerStore _bleDiscoveredPeerStore;
    private readonly object _sync = new();
    private readonly ILogger<WindowsBluetoothTransport> _transportLogger;
    private ITransport? _instance;
    private CompressedNetworkId? _localNetworkId;

    public BluetoothTransportRegistration(ILoggerFactory loggerFactory, IBleDiscoveredPeerStore bleDiscoveredPeerStore)
    {
        _bleDiscoveredPeerStore = bleDiscoveredPeerStore;
        _transportLogger = loggerFactory.CreateLogger<WindowsBluetoothTransport>();
        PeripheralScanner = new WindowsBluetoothLeShortP2PScanner(
            loggerFactory.CreateLogger<WindowsBluetoothLeShortP2PScanner>());
        try
        {
            ApplySettings(new P2pRoutingSettings());
        }
        catch
        {
            _instance = null;
        }
    }

    public IBleShortP2PPeripheralScanner PeripheralScanner { get; }

    public ITransport? Instance => Current;

    public async ValueTask DisposeAsync()
    {
        ITransport? t;
        lock (_sync)
        {
            t = _instance;
            _instance = null;
        }

        if (t != null)
            await t.DisposeAsync().ConfigureAwait(false);
    }

    public ITransport? Current
    {
        get
        {
            lock (_sync)
            {
                return _instance;
            }
        }
    }

    public void SetLocalNetworkId(CompressedNetworkId? networkId)
    {
        lock (_sync)
        {
            _localNetworkId = networkId;
        }
    }

    public void ApplySettings(P2pRoutingSettings settings)
    {
        lock (_sync)
        {
            if (_instance != null)
            {
                try
                {
                    _instance.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
                catch
                {
                    // ignore
                }

                _instance = null;
            }

            if (!settings.EnableBluetoothTransport)
                return;

            ulong? localAddr = null;
            try
            {
                // localAddr = LocalAdapterBluetoothMac
                //     .TryGetAdapterAddressAsync(settings.SelectedBluetoothAdapterDeviceId)
                //     .GetAwaiter().GetResult();
            }
            catch
            {
                // ignore
            }

            _instance = new WindowsBluetoothTransport(new WindowsBluetoothTransportOptions(
                true,
                localAddr,
                _localNetworkId,
                OnPeerNetworkIdReceived,
                _transportLogger));
        }
    }

    private void OnPeerNetworkIdReceived(TransportAddress addr, CompressedNetworkId peerNetworkId)
    {
        _ = _bleDiscoveredPeerStore.RecordScanSeenAsync(addr, new BleAdScanResult { NetworkId = peerNetworkId });
    }
}