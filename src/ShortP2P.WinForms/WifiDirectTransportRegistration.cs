using Microsoft.Extensions.Logging;
using ShortP2P.Auth.Data;
using ShortP2P.Client.WifiDirect;
using ShortP2P.Client.Routing;
using ShortP2P.Discovery.Ble;
using ShortP2P.Discovery.RouteTables;
using ShortP2P.Transport;
using ShortP2P.Transport.Abstractions;
using ShortP2P.Transport.WifiDirect.Windows;

namespace ShortP2P.WinForms;

internal sealed class WifiDirectTransportRegistration : IAsyncDisposable, IWifiDirectTransportProvider
{
    private readonly IBleDiscoveredPeerStore _discoveredPeerStore;
    private readonly IPeerRouteWriter _peerRouteWriter;
    private readonly object _sync = new();
    private readonly ILogger<WindowsWifiDirectTransport> _transportLogger;
    private ITransport? _instance;
    private CompressedNetworkId? _localNetworkId;

    public WifiDirectTransportRegistration(
        ILoggerFactory loggerFactory,
        IBleDiscoveredPeerStore discoveredPeerStore,
        IPeerRouteWriter peerRouteWriter)
    {
        _discoveredPeerStore = discoveredPeerStore;
        _peerRouteWriter = peerRouteWriter;
        _transportLogger = loggerFactory.CreateLogger<WindowsWifiDirectTransport>();
        try
        {
            ApplySettings(new P2pRoutingSettings());
        }
        catch
        {
            _instance = null;
        }
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

            _instance = new WindowsWifiDirectTransport(new WindowsWifiDirectTransportOptions(
                _localNetworkId,
                OnPeerNetworkIdReceived,
                _transportLogger));
        }
    }

    private void OnPeerNetworkIdReceived(TransportAddress addr, CompressedNetworkId peerNetworkId)
    {
        _ = _discoveredPeerStore.RecordScanSeenAsync(addr, new BleAdScanResult { NetworkId = peerNetworkId });
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
