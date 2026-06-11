using Microsoft.Extensions.Logging;
using ShortP2P.Auth.Data;
using ShortP2P.Client.Routing;
using ShortP2P.Client.WifiDirect;
using ShortP2P.Discovery.Ble;
using ShortP2P.Discovery.RouteTables;
using ShortP2P.Transport;
using ShortP2P.Transport.Abstractions;
#if WINDOWS
using ShortP2P.Transport.WifiDirect.Windows;
#endif

namespace ShortP2P.MauiApp;

internal sealed class MauiWifiDirectTransportRegistration : IAsyncDisposable, IWifiDirectTransportProvider
{
#if WINDOWS
    private readonly ILogger<WindowsWifiDirectTransport> _transportLogger;
#endif
    private readonly IBleDiscoveredPeerStore? _discoveredPeerStore;
    private readonly IPeerRouteWriter? _peerRouteWriter;
    private readonly object _sync = new();
    private ITransport? _instance;
    private CompressedNetworkId? _localNetworkId;

    public MauiWifiDirectTransportRegistration(
        ILoggerFactory loggerFactory,
        IBleDiscoveredPeerStore? discoveredPeerStore = null,
        IPeerRouteWriter? peerRouteWriter = null)
    {
        _discoveredPeerStore = discoveredPeerStore;
        _peerRouteWriter = peerRouteWriter;
#if WINDOWS
        _transportLogger = loggerFactory.CreateLogger<WindowsWifiDirectTransport>();
        try
        {
            ApplySettings(new P2pRoutingSettings());
        }
        catch
        {
            _instance = null;
        }
#endif
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

            if (!settings.EnableWifiDirectTransport)
                return;

#if WINDOWS
            _instance = new WindowsWifiDirectTransport(new WindowsWifiDirectTransportOptions(
                _localNetworkId,
                OnPeerNetworkIdReceived,
                _transportLogger));
#endif
        }
    }

    private void OnPeerNetworkIdReceived(TransportAddress addr, CompressedNetworkId peerNetworkId)
    {
        if (_discoveredPeerStore != null)
            _ = _discoveredPeerStore.RecordScanSeenAsync(addr, new BleAdScanResult { NetworkId = peerNetworkId });
        if (_peerRouteWriter != null)
            _ = _peerRouteWriter.AddOrUpdatePeerRouteAsync(
                peerNetworkId,
                WifiDirectTransportAddress.ToAddressString(addr.Data),
                null,
                TransportKind.WifiDirect);
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
