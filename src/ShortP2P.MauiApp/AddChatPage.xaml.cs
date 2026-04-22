using Microsoft.Extensions.Logging;
using ShortP2P.Client.Qr;
using ShortP2P.Client.Services;

namespace ShortP2P.MauiApp;

public partial class AddChatPage : ContentPage
{
    private readonly AuthService _auth;
    private readonly ChatRepository _chats;
    private readonly UserP2pRuntime _p2p;
    private readonly ILogger<AddChatPage> _logger;

    public AddChatPage(AuthService auth, ChatRepository chats, UserP2pRuntime p2p, ILogger<AddChatPage> logger)
    {
        InitializeComponent();
        _auth = auth;
        _chats = chats;
        _p2p = p2p;
        _logger = logger;
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
            _logger.LogWarning("QR decode failed from file: {Error}", err);
            await DisplayAlert("QR", err ?? "Could not read QR code.", "OK").ConfigureAwait(true);
            return;
        }

        ApplyQrPayload(payload);
        await TryInstallChatFromQrAsync(payload).ConfigureAwait(true);
    }

    private async void OnScanQrCameraClicked(object? sender, EventArgs e)
    {
        if (!await EnsureCameraPermissionAsync().ConfigureAwait(true))
            return;

#if ANDROID
        try
        {
            var qrText = await MainActivity.TryScanQrWithSystemScannerAsync().ConfigureAwait(true);
            if (!string.IsNullOrWhiteSpace(qrText))
            {
                if (PeerQrCodec.TryDeserialize(qrText.Trim(), out var payloadFromScanner, out var errFromScanner))
                {
                    ApplyQrPayload(payloadFromScanner);
                    await TryInstallChatFromQrAsync(payloadFromScanner).ConfigureAwait(true);
                    return;
                }

                _logger.LogWarning("System QR scanner returned invalid payload: {Error}", errFromScanner);
                await DisplayAlert("QR", errFromScanner ?? "Scanned QR has invalid format.", "OK").ConfigureAwait(true);
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "System QR scanner invocation failed");
        }
#endif

        FileResult? photo;
        try
        {
            if (!MediaPicker.Default.IsCaptureSupported)
            {
                await DisplayAlert("Camera", "Camera capture is not supported on this device.", "OK")
                    .ConfigureAwait(true);
                return;
            }

            photo = await MediaPicker.Default.CapturePhotoAsync().ConfigureAwait(true);
        }
        catch (PermissionException ex)
        {
            _logger.LogWarning(ex, "Camera permission denied");
            await DisplayAlert("Camera", "Camera permission is required to scan QR.", "OK").ConfigureAwait(true);
            return;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Camera capture failed");
            await DisplayAlert("Camera", $"Could not open camera: {ex.Message}", "OK").ConfigureAwait(true);
            return;
        }

        if (photo == null)
            return;

        await using var stream = await photo.OpenReadAsync().ConfigureAwait(true);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms).ConfigureAwait(true);
        var bytes = ms.ToArray();

        if (!PeerQrService.TryDecodeImage(bytes, out var payload, out var err))
        {
            _logger.LogWarning("QR decode failed from camera photo: {Error}", err);
            await DisplayAlert("QR", err ?? "Could not read QR code from photo.", "OK").ConfigureAwait(true);
            return;
        }

        ApplyQrPayload(payload);
        await TryInstallChatFromQrAsync(payload).ConfigureAwait(true);
    }

    private async Task<bool> EnsureCameraPermissionAsync()
    {
        try
        {
            var status = await Permissions.CheckStatusAsync<Permissions.Camera>().ConfigureAwait(true);
            if (status != PermissionStatus.Granted)
                status = await Permissions.RequestAsync<Permissions.Camera>().ConfigureAwait(true);
            if (status == PermissionStatus.Granted)
                return true;
            await DisplayAlert("Camera", "Camera permission is required to scan QR.", "OK").ConfigureAwait(true);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Camera permission request failed");
            await DisplayAlert("Camera", "Could not request camera permission.", "OK").ConfigureAwait(true);
            return false;
        }
    }

    private void ApplyQrPayload(PeerQrPayload payload)
    {
        PeerNickEntry.Text = payload.N;
        PeerIdEntry.Text = payload.Id;
        PeerPubKeyEditor.Text = payload.K;
        PeerHostEntry.Text = payload.GetCommaSeparatedHosts();
        PeerPortEntry.Text = payload.P.ToString();
    }

    private async Task TryInstallChatFromQrAsync(PeerQrPayload payload)
    {
        var u = _auth.CurrentUser;
        if (u == null)
            return;

        try
        {
            var chat = await _chats
                .AddChatAsync(u.Id, payload.N, payload.Id, payload.K, payload.GetCommaSeparatedHosts(), payload.P)
                .ConfigureAwait(true);

            await _p2p.EnsureStartedAsync(u).ConfigureAwait(true);
            var session = _p2p.GetOrCreateSession(chat, u, _auth, _chats, SynchronizationContext.Current);
            if (!_p2p.IsChatSessionStarted(chat.Id))
            {
                await session.StartAsync().ConfigureAwait(true);
                _p2p.MarkChatSessionStarted(chat.Id);
            }

            await Navigation.PopModalAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "QR auto-install failed");
            await DisplayAlert("QR", $"Could not auto-install chat: {ex.Message} {ex.StackTrace}", "OK").ConfigureAwait(true);
        }
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
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invalid public key when saving chat");
            await DisplayAlert("Error", "Invalid public key JSON.", "OK").ConfigureAwait(true);
            return;
        }

        await _chats.AddChatAsync(u.Id, nick, id, pub, host, port).ConfigureAwait(true);
        await Navigation.PopModalAsync().ConfigureAwait(true);
    }
}
