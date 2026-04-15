using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShortP2P.Client.Services;

namespace ShortP2P.MauiApp;

public partial class LoginPage : ContentPage
{
    private readonly AuthService _auth;
    private readonly ILogger<LoginPage> _logger;

    public LoginPage(AuthService auth, ILogger<LoginPage> logger)
    {
        InitializeComponent();
        _auth = auth;
        _logger = logger;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (await _auth.TryRestoreSessionAsync().ConfigureAwait(true) && _auth.CurrentUser != null)
            GoToChats();
    }

    private async void OnLoginClicked(object? sender, EventArgs e)
    {
        var nick = NicknameEntry.Text?.Trim() ?? "";
        var pass = PasswordEntry.Text ?? "";
        var (ok, err) = await _auth.LoginAsync(nick, pass).ConfigureAwait(true);
        if (!ok)
        {
            _logger.LogWarning("Login failed for {Nickname}: {Reason}", nick, err);
            await DisplayAlert("Login", err ?? "Failed", "OK").ConfigureAwait(true);
            return;
        }

        GoToChats();
    }

    private async void OnRegisterNavigateClicked(object? sender, EventArgs e)
    {
        var page = MauiProgram.Services.GetRequiredService<RegisterPage>();
        await Navigation.PushAsync(page).ConfigureAwait(true);
    }

    private void GoToChats()
    {
        var chats = MauiProgram.Services.GetRequiredService<ChatsPage>();
        Application.Current!.MainPage = new NavigationPage(chats);
    }
}
