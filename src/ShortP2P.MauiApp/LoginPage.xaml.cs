using Microsoft.Extensions.DependencyInjection;
using ShortP2P.Client.Services;

namespace ShortP2P.MauiApp;

public partial class LoginPage : ContentPage
{
    private readonly AuthService _auth;

    public LoginPage(AuthService auth)
    {
        InitializeComponent();
        _auth = auth;
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
