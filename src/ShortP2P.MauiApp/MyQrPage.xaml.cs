using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using ShortP2P.Auth;
using ShortP2P.Auth.Data;
using ShortP2P.Client.Bluetooth;
using ShortP2P.Client.Qr;
using ShortP2P.Client.Services;
using ShortP2P.Crypto;
using ShortP2P.Transport;

namespace ShortP2P.MauiApp;

public partial class MyQrPage : ContentPage
{
    private readonly AuthService _auth;
    private readonly UserP2pRuntime _runtime;
    private readonly IBluetoothRadioCatalog _bluetoothCatalog;
    private readonly ILogger<MyQrPage> _logger;
    private byte[]? _currentQrPng;

    public MyQrPage(AuthService auth, UserP2pRuntime runtime, IBluetoothRadioCatalog bluetoothCatalog,
        ILogger<MyQrPage> logger)
    {
        InitializeComponent();
        _auth = auth;
        _runtime = runtime;
        _bluetoothCatalog = bluetoothCatalog;
        _logger = logger;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        var u = _auth.CurrentUser;
        if (u == null)
        {
            _logger.LogWarning("My QR: user not logged in");
            return;
        }

        var pub = RsaKeySerializer.SerializePublic(_auth.GetCurrentPublicKey());
        string? btMac = null;
        try
        {
            btMac = await BluetoothRoutingMac.GetEffectiveMacAsync(_runtime.Settings, _bluetoothCatalog)
                .ConfigureAwait(true);
        }
        catch
        {
            // ignore
        }

        RenderQr(u, pub, btMac);
    }

    private void RenderQr(UserEntity u, string pub, string? bluetoothMac)
    {
        var payload = PeerQrService.BuildPayload(u, pub, null, bluetoothMac, null);
        var png = PeerQrService.EncodeQrPng(payload);
        _currentQrPng = png;
        QrImage.Source = ImageSource.FromStream(() => new MemoryStream(png));
    }

    private async void OnShareQrClicked(object? sender, EventArgs e)
    {
        if (_currentQrPng == null || _currentQrPng.Length == 0)
        {
            await DisplayAlert("QR", "QR-код пока не готов.", "OK").ConfigureAwait(true);
            return;
        }

        try
        {
            var filename = $"shortp2p-my-qr-{DateTime.UtcNow:yyyyMMddHHmmss}.png";
            var path = Path.Combine(FileSystem.CacheDirectory, filename);
            await File.WriteAllBytesAsync(path, _currentQrPng).ConfigureAwait(true);
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Поделиться QR-кодом",
                File = new ShareFile(path),
            }).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Share QR failed");
            await DisplayAlert("QR", $"Не удалось поделиться QR-кодом: {ex.Message}", "OK").ConfigureAwait(true);
        }
    }
}
