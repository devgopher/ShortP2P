using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShortP2P.Client.Data;
using ShortP2P.Client.Services;

namespace ShortP2P.MauiApp;

public partial class ChatsPage : ContentPage
{
    private readonly AuthService _auth;
    private readonly ChatRepository _chats;
    private readonly UserP2pRuntime _p2p;
    private readonly ILogger<ChatsPage> _logger;

    public ChatsPage(AuthService auth, ChatRepository chats, UserP2pRuntime p2p, ILogger<ChatsPage> logger)
    {
        InitializeComponent();
        _auth = auth;
        _chats = chats;
        _p2p = p2p;
        _logger = logger;
    }

    private void OnChatListChangedFromInvite(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(() => _ = OnChatListChangedAsync());

    private async Task OnChatListChangedAsync()
    {
        await RefreshAsync().ConfigureAwait(true);
        var u = _auth.CurrentUser;
        if (u == null)
            return;
        try
        {
            await _p2p.EnsureAllChatSessionsStartedAsync(u, _auth, _chats, SynchronizationContext.Current)
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ensure chat sessions after list change");
        }
    }

    private async void OnRoutingClicked(object? sender, EventArgs e)
    {
        var page = MauiProgram.Services.GetRequiredService<RoutingSettingsPage>();
        await Navigation.PushAsync(page).ConfigureAwait(true);
    }

    private async void OnLanScanClicked(object? sender, EventArgs e)
    {
        var page = MauiProgram.Services.GetRequiredService<LanScanPage>();
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
                await _p2p.EnsureAllChatSessionsStartedAsync(u, _auth, _chats, SynchronizationContext.Current)
                    .ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ensure P2P on chats page appearing");
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

    private async void OnChatSwipeDelete(object? sender, EventArgs e)
    {
        ChatEntity? chat = null;
        for (var p = sender as Element; p != null; p = p.Parent)
        {
            if (p is SwipeView sw && sw.BindingContext is ChatEntity c)
            {
                chat = c;
                break;
            }
        }

        if (chat == null)
            return;

        var u = _auth.CurrentUser;
        if (u == null)
            return;

        var confirm = await DisplayAlert("Delete chat",
            $"Remove «{chat.PeerNickname}» only on this device? All messages will be deleted.",
            "Delete",
            "Cancel").ConfigureAwait(true);
        if (!confirm)
            return;

        await _p2p.RemoveChatSessionAsync(chat.Id).ConfigureAwait(true);
        await _chats.DeleteChatAsync(chat.Id, u.Id).ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
    }

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        try
        {
            await _p2p.StopAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Stop P2P on logout");
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
