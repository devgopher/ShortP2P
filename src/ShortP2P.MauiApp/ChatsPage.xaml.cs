using Microsoft.Extensions.DependencyInjection;
using ShortP2P.Client.Data;
using ShortP2P.Client.Services;

namespace ShortP2P.MauiApp;

public partial class ChatsPage : ContentPage
{
    private readonly AuthService _auth;
    private readonly ChatRepository _chats;
    private readonly UserP2pRuntime _p2p;

    public ChatsPage(AuthService auth, ChatRepository chats, UserP2pRuntime p2p)
    {
        InitializeComponent();
        _auth = auth;
        _chats = chats;
        _p2p = p2p;
    }

    private void OnChatListChangedFromInvite(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(() => _ = RefreshAsync());

    private async void OnRoutingClicked(object? sender, EventArgs e)
    {
        var page = MauiProgram.Services.GetRequiredService<RoutingSettingsPage>();
        await Navigation.PushAsync(page).ConfigureAwait(true);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _chats.ChatListChanged -= OnChatListChangedFromInvite;
        _chats.ChatListChanged += OnChatListChangedFromInvite;
        var u = _auth.CurrentUser;
        if (u != null)
        {
            try
            {
                await _p2p.EnsureStartedAsync(u).ConfigureAwait(true);
            }
            catch
            {
                // UDP/discovery may fail on some devices; chat may still work for direct hosts
            }
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    protected override void OnDisappearing()
    {
        _chats.ChatListChanged -= OnChatListChangedFromInvite;
        base.OnDisappearing();
    }

    private async Task RefreshAsync()
    {
        var u = _auth.CurrentUser;
        if (u == null)
        {
            Application.Current!.MainPage = new NavigationPage(MauiProgram.Services.GetRequiredService<LoginPage>());
            return;
        }

        ProfileLabel.Text =
            $"You: {u.Nickname} · id {u.NetworkIdShort} · local UDP {u.DataUdpPort}";

        var list = await _chats.ListChatsAsync(u.Id).ConfigureAwait(true);
        ChatsCollection.ItemsSource = list;
    }

    private async void OnAddChatClicked(object? sender, EventArgs e)
    {
        var page = MauiProgram.Services.GetRequiredService<AddChatPage>();
        await Navigation.PushModalAsync(new NavigationPage(page)).ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
    }

    private async void OnMyQrClicked(object? sender, EventArgs e)
    {
        var page = MauiProgram.Services.GetRequiredService<MyQrPage>();
        await Navigation.PushAsync(page).ConfigureAwait(true);
    }

    private async void OnCopyKeysClicked(object? sender, EventArgs e)
    {
        var u = _auth.CurrentUser;
        if (u == null) return;
        var pub = RsaKeySerializer.SerializePublic(_auth.GetCurrentPublicKey());
        var text = $"Network id: {u.NetworkIdShort}\nPublic key JSON:\n{pub}";
        await Clipboard.Default.SetTextAsync(text).ConfigureAwait(true);
        await DisplayAlert("Copied", "Network id and public key JSON copied to clipboard.", "OK").ConfigureAwait(true);
    }

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        try
        {
            await _p2p.StopAsync().ConfigureAwait(true);
        }
        catch
        {
        }

        await _auth.LogoutAsync().ConfigureAwait(true);
        Application.Current!.MainPage = new NavigationPage(MauiProgram.Services.GetRequiredService<LoginPage>());
    }

    private async void OnChatSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not ChatEntity chat)
            return;

        ChatsCollection.SelectedItem = null;
        var page = MauiProgram.Services.GetRequiredService<ChatDetailPage>();
        page.ChatId = chat.Id;
        await Navigation.PushAsync(page).ConfigureAwait(true);
    }
}
