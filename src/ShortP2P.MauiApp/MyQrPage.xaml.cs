using Microsoft.Extensions.Logging;
using ShortP2P.Auth;
using ShortP2P.Auth.Data;
using ShortP2P.Client.Qr;
using ShortP2P.Crypto;
#if WINDOWS
using ShortP2P.Transport.Bluetooth.Windows;
#endif
#if ANDROID
using Android.Bluetooth;
#endif

namespace ShortP2P.MauiApp;

public partial class MyQrPage : ContentPage
{
    private readonly AuthService _auth;
    private readonly ILogger<MyQrPage> _logger;

    public MyQrPage(AuthService auth, ILogger<MyQrPage> logger)
    {
        InitializeComponent();
        _auth = auth;
        _logger = logger;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        var u = _auth.CurrentUser;
        if (u == null)
        {
            _logger.LogWarning("My QR: user not logged in");
            return;
        }

        var pub = RsaKeySerializer.SerializePublic(_auth.GetCurrentPublicKey());
#if WINDOWS
        _ = ShowQrWithWindowsBluetoothAsync(u, pub);
#else
        RenderQr(u, pub, null);
#endif
    }

#if WINDOWS
    private async Task ShowQrWithWindowsBluetoothAsync(UserEntity u, string pub)
    {
        string? btMac = null;
        try
        {
            btMac = await LocalAdapterBluetoothMac.TryGetAdapterMacStringAsync().ConfigureAwait(true);
        }
        catch
        {
            // ignore
        }

        RenderQr(u, pub, btMac);
    }
#endif

    private void RenderQr(UserEntity u, string pub, string? bluetoothMac)
    {
#if ANDROID
        bluetoothMac ??= TryGetAndroidBluetoothMac();
#endif
        var payload = PeerQrService.BuildPayload(u, pub, null, bluetoothMac, null);
        var png = PeerQrService.EncodeQrPng(payload);
        QrImage.Source = ImageSource.FromStream(() => new MemoryStream(png));
    }

#if ANDROID
    private static string? TryGetAndroidBluetoothMac()
    {
        try
        {
            var a = BluetoothAdapter.DefaultAdapter;
            if (a == null)
                return null;
            var addr = a.Address;
            if (string.IsNullOrWhiteSpace(addr))
                return null;
            if (string.Equals(addr, "02:00:00:00:00:00", StringComparison.OrdinalIgnoreCase))
                return null;
            return addr;
        }
        catch
        {
            return null;
        }
    }
#endif
}
