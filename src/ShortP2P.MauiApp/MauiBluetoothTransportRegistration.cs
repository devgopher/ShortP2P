using Microsoft.Extensions.Logging;
using ShortP2P.Auth.Data;
using ShortP2P.Client.Bluetooth;
using ShortP2P.Client.Routing;
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
#endif
    private readonly object _sync = new();
    private ITransport? _instance;
    private CompressedNetworkId? _localNetworkId;

    public ITransport? Current
    {
        get
        {
            lock (_sync)
                return _instance;
        }
    }

    public MauiBluetoothTransportRegistration(ILoggerFactory loggerFactory)
    {
#if WINDOWS
        _transportLogger = loggerFactory.CreateLogger<WindowsBluetoothTransport>();
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
                Logger: _transportLogger));
#elif ANDROID
            _instance = new AndroidBluetoothTransport(global::Android.App.Application.Context,
                new AndroidBluetoothTransportOptions(GattDiscoverable: true, LocalNetworkId: _localNetworkId));
#endif
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
