using ShortP2P.Auth;
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
            await GoToChatsAsync().ConfigureAwait(true);
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

        await GoToChatsAsync().ConfigureAwait(true);
    }

    private async void OnRegisterNavigateClicked(object? sender, EventArgs e)
    {
        var page = MauiProgram.Services.GetRequiredService<RegisterPage>();
        await Navigation.PushAsync(page).ConfigureAwait(true);
    }

    private async Task GoToChatsAsync()
    {
        var user = _auth.CurrentUser;
        if (user != null)
            try
            {
                var p2p = MauiProgram.Services.GetRequiredService<UserP2pRuntime>();
                var chatsRepo = MauiProgram.Services.GetRequiredService<ChatRepository>();
                await p2p.EnsureStartedAsync(user).ConfigureAwait(true);
                await p2p.EnsureAllChatSessionsStartedAsync(user, _auth, chatsRepo, SynchronizationContext.Current)
                    .ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ensure P2P sessions on login");
            }

        var chats = MauiProgram.Services.GetRequiredService<ChatsPage>();
        Application.Current!.MainPage = new NavigationPage(chats);
    }
}