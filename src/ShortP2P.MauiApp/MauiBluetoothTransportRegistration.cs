using Microsoft.Extensions.Logging;
using ShortP2P.Auth.Data;
using ShortP2P.Client.Bluetooth;
using ShortP2P.Client.Routing;
using ShortP2P.Discovery;
using ShortP2P.Discovery.Ble;
using ShortP2P.Transport.Abstractions;
#if ANDROID
using ShortP2P.Transport.Bluetooth.Android;
#endif

#if WINDOWS
using ShortP2P.Transport.Bluetooth.Windows;
#endif

namespace ShortP2P.MauiApp;

internal sealed class MauiBluetoothTransportRegistration : IAsyncDisposable, IBluetoothTransportProvider
{
#if WINDOWS
    private readonly ILogger<WindowsBluetoothTransport> _transportLogger;
#elif ANDROID
    private readonly ILogger<AndroidBluetoothTransport> _transportLogger;
#endif
    private readonly object _sync = new();
    private ITransport? _instance;
    private CompressedNetworkId? _localNetworkId;
    private IBleDiscoveredPeerStore? _bleDiscoveredPeerStore;

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

    public MauiBluetoothTransportRegistration(ILoggerFactory loggerFactory,
        IBleDiscoveredPeerStore? bleDiscoveredPeerStore = null)
    {
        _bleDiscoveredPeerStore = bleDiscoveredPeerStore;
#if WINDOWS
        _transportLogger = loggerFactory.CreateLogger<WindowsBluetoothTransport>();
#elif ANDROID
        _transportLogger = loggerFactory.CreateLogger<AndroidBluetoothTransport>();
#endif
        try
        {
            ApplySettings(new P2pRoutingSettings());
        }
        catch
        {
            _instance = null;
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

#if WINDOWS
            ulong? localAddr = null;
            try
            {
                localAddr = LocalAdapterBluetoothMac
                    .TryGetAdapterAddressAsync(settings.SelectedBluetoothAdapterDeviceId)
                    .GetAwaiter().GetResult();
            }
            catch
            {
                // ignore
            }

            _instance = new WindowsBluetoothTransport(new WindowsBluetoothTransportOptions(
                GattDiscoverable: true,
                LocalAdapterBluetoothAddress: localAddr,
                LocalNetworkId: _localNetworkId,
                OnPeerNetworkIdReceived: OnPeerNetworkIdReceived,
                Logger: _transportLogger));
#elif ANDROID
            _instance = new AndroidBluetoothTransport(global::Android.App.Application.Context,
                new AndroidBluetoothTransportOptions(
                    GattDiscoverable: true,
                    LocalNetworkId: _localNetworkId,
                    OnPeerNetworkIdReceived: OnPeerNetworkIdReceived,
                    Logger: _transportLogger));
#endif
        }
    }

    private void OnPeerNetworkIdReceived(TransportAddress addr, CompressedNetworkId peerNetworkId)
    {
        if (_bleDiscoveredPeerStore == null)
            return;
        _ = _bleDiscoveredPeerStore.RecordScanSeenAsync(addr, new BleAdScanResult { NetworkId = peerNetworkId });
    }

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
}