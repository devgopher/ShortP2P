using Microsoft.Extensions.Logging;
using ShortP2P.Auth;
using ShortP2P.Auth.Data;

namespace ShortP2P.MauiApp;

public sealed class ProfilePage : ContentPage
{
    private readonly AuthService _auth;
    private readonly ILogger<ProfilePage> _logger;
    private readonly Editor _aboutMe;
    private readonly Label _aboutCounter;
    private readonly Image _avatarPreview;
    private readonly Label _status;
    private byte[]? _avatarBytes;

    public ProfilePage(AuthService auth, ILogger<ProfilePage> logger)
    {
        _auth = auth;
        _logger = logger;
        Title = "My profile";

        _avatarPreview = new Image
        {
            WidthRequest = 96,
            HeightRequest = 96,
            Aspect = Aspect.AspectFill,
            HorizontalOptions = LayoutOptions.Start
        };
        _aboutMe = new Editor
        {
            AutoSize = EditorAutoSizeOption.TextChanges,
            HeightRequest = 100,
            Placeholder = $"About me (max {PeerProfileLimits.MaxAboutMeChars} chars)"
        };
        _aboutCounter = new Label { FontSize = 12, TextColor = Colors.Gray };
        _status = new Label { FontSize = 12, TextColor = Colors.Gray };

        var pick = new Button { Text = "Choose avatar…" };
        var clear = new Button { Text = "Clear avatar" };
        var save = new Button { Text = "Save" };
        pick.Clicked += async (_, _) => await OnPickAvatarAsync().ConfigureAwait(true);
        clear.Clicked += (_, _) =>
        {
            _avatarBytes = null;
            _avatarPreview.Source = null;
        };
        save.Clicked += async (_, _) => await OnSaveAsync().ConfigureAwait(true);
        _aboutMe.TextChanged += (_, _) => UpdateCounter();

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 16,
                Spacing = 10,
                Children =
                {
                    _avatarPreview,
                    pick,
                    clear,
                    new Label
                    {
                        Text =
                            $"Avatar ≤ {PeerProfileLimits.MaxAvatarBytes / 1024} KB. Stored locally only; shared on LAN scan.",
                        FontSize = 12,
                        TextColor = Colors.Gray
                    },
                    _aboutMe,
                    _aboutCounter,
                    save,
                    _status
                }
            }
        };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        LoadCurrentProfileBestEffort();
    }

    private void LoadCurrentProfileBestEffort()
    {
        try
        {
            var user = _auth.CurrentUser;
            if (user == null)
                return;
            _aboutMe.Text = user.AboutMe ?? "";
            _avatarBytes = user.Avatar;
            ApplyAvatarPreview(_avatarBytes);
            UpdateCounter();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load own profile into UI (best-effort); continuing");
            _aboutMe.Text = "";
            _avatarBytes = null;
            _avatarPreview.Source = null;
            UpdateCounter();
        }
    }

    private void UpdateCounter()
    {
        var len = _aboutMe.Text?.Length ?? 0;
        _aboutCounter.Text = $"{len} / {PeerProfileLimits.MaxAboutMeChars}";
    }

    private async Task OnPickAvatarAsync()
    {
        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Choose avatar",
                FileTypes = FilePickerFileType.Images
            }).ConfigureAwait(true);
            if (result == null)
                return;

            await using var stream = await result.OpenReadAsync().ConfigureAwait(true);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms).ConfigureAwait(true);
            var bytes = ms.ToArray();
            if (bytes.Length > PeerProfileLimits.MaxAvatarBytes)
            {
                _status.Text = $"File larger than {PeerProfileLimits.MaxAvatarBytes / 1024} KB.";
                return;
            }

            _avatarBytes = bytes;
            ApplyAvatarPreview(bytes);
            _status.Text = "";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to pick avatar (best-effort)");
            _status.Text = "Could not load image.";
        }
    }

    private void ApplyAvatarPreview(byte[]? bytes)
    {
        try
        {
            if (bytes == null || bytes.Length == 0)
            {
                _avatarPreview.Source = null;
                return;
            }

            _avatarPreview.Source = ImageSource.FromStream(() => new MemoryStream(bytes));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to preview avatar (best-effort)");
            _avatarPreview.Source = null;
        }
    }

    private async Task OnSaveAsync()
    {
        try
        {
            var text = _aboutMe.Text ?? "";
            if (text.Length > PeerProfileLimits.MaxAboutMeChars)
                text = text[..PeerProfileLimits.MaxAboutMeChars];

            var (ok, error) = await _auth.UpdateProfileAsync(text, _avatarBytes).ConfigureAwait(true);
            if (!ok)
            {
                _status.Text = error ?? "Save failed";
                return;
            }

            _status.Text = "Saved.";
            await Navigation.PopAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save own profile");
            _status.Text = "Could not save profile.";
        }
    }
}
