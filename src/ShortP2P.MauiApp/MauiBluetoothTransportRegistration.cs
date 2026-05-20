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
    private readonly object _sync = new();
    private ITransport? _instance;

    public ITransport? Current
    {
        get
        {
            lock (_sync)
                return _instance;
        }
    }

    public MauiBluetoothTransportRegistration()
    {
        try
        {
            ApplySettings(new P2pRoutingSettings());
        }
        catch
        {
            _instance = null;
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
                LocalAdapterBluetoothAddress: localAddr));
#elif ANDROID
            _instance = new AndroidBluetoothTransport(global::Android.App.Application.Context);
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
