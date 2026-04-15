using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShortP2P.Client.Services;

namespace ShortP2P.MauiApp;

public partial class RegisterPage : ContentPage
{
    private readonly AuthService _auth;
    private readonly ILogger<RegisterPage> _logger;

    public RegisterPage(AuthService auth, ILogger<RegisterPage> logger)
    {
        InitializeComponent();
        _auth = auth;
        _logger = logger;
    }

    private async void OnRegisterClicked(object? sender, EventArgs e)
    {
        var nick = NicknameEntry.Text?.Trim() ?? "";
        var pass = PasswordEntry.Text ?? "";
        var (ok, err) = await _auth.RegisterAsync(nick, pass).ConfigureAwait(true);
        if (!ok)
        {
            _logger.LogWarning("Registration failed for {Nickname}: {Reason}", nick, err);
            await DisplayAlert("Register", err ?? "Failed", "OK").ConfigureAwait(true);
            return;
        }

        var id = _auth.CurrentUser?.NetworkIdShort ?? "";
        await DisplayAlert("Account created", $"Your network id:\n{id}", "OK").ConfigureAwait(true);

        var chats = MauiProgram.Services.GetRequiredService<ChatsPage>();
        Application.Current!.MainPage = new NavigationPage(chats);
    }
}
