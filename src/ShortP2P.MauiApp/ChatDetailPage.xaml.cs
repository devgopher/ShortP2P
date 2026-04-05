using ShortP2P.Client.Data;
using ShortP2P.Client.Services;

namespace ShortP2P.MauiApp;

public partial class ChatDetailPage : ContentPage
{
    private readonly AuthService _auth;
    private readonly ChatRepository _repo;
    private readonly UserP2pRuntime _p2p;
    private ChatP2pSession? _p2pSession;

    public ChatDetailPage(AuthService auth, ChatRepository repo, UserP2pRuntime p2p)
    {
        InitializeComponent();
        _auth = auth;
        _repo = repo;
        _p2p = p2p;
    }

    public int ChatId { get; set; }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        var chat = await _repo.GetChatAsync(ChatId).ConfigureAwait(true);
        if (chat == null)
        {
            await DisplayAlert("Error", "Chat not found.", "OK").ConfigureAwait(true);
            await Navigation.PopAsync().ConfigureAwait(true);
            return;
        }

        Title = chat.PeerNickname;
        var user = _auth.CurrentUser;
        if (user == null)
        {
            await Navigation.PopAsync().ConfigureAwait(true);
            return;
        }

        var uiSync = SynchronizationContext.Current;
        _p2pSession = new ChatP2pSession(chat, user, _auth, _repo, uiSync, _p2p.Gateway, _p2p.Settings);
        _p2pSession.MessagesChanged += OnP2PMessagesChanged;
        try
        {
            await _p2pSession.StartAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            await DisplayAlert("P2P", $"Could not start UDP: {ex.Message}", "OK").ConfigureAwait(true);
        }

        await ReloadMessagesAsync().ConfigureAwait(true);
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        if (_p2pSession != null)
        {
            _p2pSession.MessagesChanged -= OnP2PMessagesChanged;
            await _p2pSession.DisposeAsync().ConfigureAwait(true);
            _p2pSession = null;
        }
    }

    private void OnP2PMessagesChanged(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(async () => await ReloadMessagesAsync().ConfigureAwait(true));

    private async Task ReloadMessagesAsync()
    {
        var rows = await _repo.ListMessagesAsync(ChatId).ConfigureAwait(true);
        MessagesCollection.ItemsSource = rows
            .Select(m => new MessageRow(m.Outgoing ? "You" : "Peer", m.Text))
            .ToList();
    }

    private async void OnSendClicked(object? sender, EventArgs e)
    {
        var text = MessageEntry.Text?.Trim() ?? "";
        if (text.Length == 0 || _p2pSession == null)
            return;

        try
        {
            await _p2pSession.SendTextAsync(text).ConfigureAwait(true);
            MessageEntry.Text = string.Empty;
            await ReloadMessagesAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            await DisplayAlert("Send failed", ex.Message, "OK").ConfigureAwait(true);
        }
    }

    private sealed record MessageRow(string DirectionLabel, string Text);
}
