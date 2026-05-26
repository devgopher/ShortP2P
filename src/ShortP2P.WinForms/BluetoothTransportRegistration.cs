using Microsoft.Extensions.Logging;
using ShortP2P.Auth.Data;
using ShortP2P.Client.Bluetooth;
using ShortP2P.Client.Routing;
using ShortP2P.Transport.Abstractions;
using ShortP2P.Transport.Bluetooth.Windows;

namespace ShortP2P.WinForms;

internal sealed class BluetoothTransportRegistration : IAsyncDisposable, IBluetoothTransportProvider
{
    private readonly ILogger<WindowsBluetoothTransport> _transportLogger;
    private readonly object _sync = new();
    private ITransport? _instance;
    private CompressedNetworkId? _localNetworkId;

    public IBleShortP2PPeripheralScanner PeripheralScanner { get; }

    public ITransport? Current
    {
        get
        {
            lock (_sync)
                return _instance;
        }
    }

    public ITransport? Instance => Current;

    public BluetoothTransportRegistration(ILoggerFactory loggerFactory)
    {
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

    public void SetLocalNetworkId(CompressedNetworkId? networkId)
    {
        lock (_sync)
            _localNetworkId = networkId;
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
                Logger: _transportLogger));
        }
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
