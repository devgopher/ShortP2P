using ShortP2P.Client.Qr;
using ShortP2P.Client.Services;

namespace ShortP2P.MauiApp;

public partial class AddChatPage : ContentPage
{
    private readonly AuthService _auth;
    private readonly ChatRepository _chats;

    public AddChatPage(AuthService auth, ChatRepository chats)
    {
        InitializeComponent();
        _auth = auth;
        _chats = chats;
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        await Navigation.PopModalAsync().ConfigureAwait(true);
    }

    private async void OnScanQrClicked(object? sender, EventArgs e)
    {
        var result = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Image with peer QR code",
            FileTypes = FilePickerFileType.Images,
        }).ConfigureAwait(true);

        if (result == null)
            return;

        await using var stream = await result.OpenReadAsync().ConfigureAwait(true);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms).ConfigureAwait(true);
        var bytes = ms.ToArray();

        if (!PeerQrService.TryDecodeImage(bytes, out var payload, out var err))
        {
            await DisplayAlert("QR", err ?? "Could not read QR code.", "OK").ConfigureAwait(true);
            return;
        }

        PeerNickEntry.Text = payload.N;
        PeerIdEntry.Text = payload.Id;
        PeerPubKeyEditor.Text = payload.K;
        PeerHostEntry.Text = payload.GetCommaSeparatedHosts();
        PeerPortEntry.Text = payload.P.ToString();
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        var u = _auth.CurrentUser;
        if (u == null)
        {
            await DisplayAlert("Error", "Not logged in.", "OK").ConfigureAwait(true);
            return;
        }

        var nick = PeerNickEntry.Text?.Trim() ?? "";
        var id = PeerIdEntry.Text?.Trim() ?? "";
        var pub = PeerPubKeyEditor.Text?.Trim() ?? "";
        var host = PeerHostEntry.Text?.Trim() ?? "";
        if (!int.TryParse(PeerPortEntry.Text, out var port))
        {
            await DisplayAlert("Error", "Invalid peer port.", "OK").ConfigureAwait(true);
            return;
        }

        if (nick.Length == 0 || id.Length == 0 || pub.Length == 0 || host.Length == 0)
        {
            await DisplayAlert("Error", "Fill all fields.", "OK").ConfigureAwait(true);
            return;
        }

        try
        {
            _ = RsaKeySerializer.DeserializePublic(pub);
        }
        catch
        {
            await DisplayAlert("Error", "Invalid public key JSON.", "OK").ConfigureAwait(true);
            return;
        }

        await _chats.AddChatAsync(u.Id, nick, id, pub, host, port).ConfigureAwait(true);
        await Navigation.PopModalAsync().ConfigureAwait(true);
    }
}
