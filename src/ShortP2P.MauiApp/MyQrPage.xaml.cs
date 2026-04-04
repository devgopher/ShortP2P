using ShortP2P.Client.Qr;
using ShortP2P.Client.Services;

namespace ShortP2P.MauiApp;

public partial class MyQrPage : ContentPage
{
    private readonly AuthService _auth;

    public MyQrPage(AuthService auth)
    {
        InitializeComponent();
        _auth = auth;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        var u = _auth.CurrentUser;
        if (u == null)
            return;

        var pub = RsaKeySerializer.SerializePublic(_auth.GetCurrentPublicKey());
        var payload = PeerQrService.BuildPayload(u, pub);
        var png = PeerQrService.EncodeQrPng(payload);
        QrImage.Source = ImageSource.FromStream(() => new MemoryStream(png));
    }
}
