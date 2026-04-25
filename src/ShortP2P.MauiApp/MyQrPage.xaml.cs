using Microsoft.Extensions.Logging;
using ShortP2P.Auth;
using ShortP2P.Client.Qr;
using ShortP2P.Crypto;

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
        var payload = PeerQrService.BuildPayload(u, pub);
        var png = PeerQrService.EncodeQrPng(payload);
        QrImage.Source = ImageSource.FromStream(() => new MemoryStream(png));
    }
}
