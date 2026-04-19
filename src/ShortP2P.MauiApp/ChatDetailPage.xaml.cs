using System.Globalization;
using Microsoft.Extensions.Logging;
using ShortP2P.Client.Data;
using ShortP2P.Client.Services;

namespace ShortP2P.MauiApp;

public partial class ChatDetailPage : ContentPage
{
    private static readonly Color PresenceOnline = Color.FromArgb("#228B22");
    private static readonly Color PresenceOffline = Color.FromArgb("#CD5C5C");

    private readonly AuthService _auth;
    private readonly ChatRepository _repo;
    private readonly UserP2pRuntime _p2p;
    private readonly ILogger<ChatDetailPage> _logger;
    private ChatP2pSession? _p2pSession;
    private string? _peerNetworkIdShort;

    public ChatDetailPage(AuthService auth, ChatRepository repo, UserP2pRuntime p2p, ILogger<ChatDetailPage> logger)
    {
        InitializeComponent();
        _auth = auth;
        _repo = repo;
        _p2p = p2p;
        _logger = logger;
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
        PeerIdLabel.Text = $"Id: {chat.PeerNetworkIdShort}";
        _peerNetworkIdShort = chat.PeerNetworkIdShort;
        var user = _auth.CurrentUser;
        if (user == null)
        {
            _peerNetworkIdShort = null;
            await Navigation.PopAsync().ConfigureAwait(true);
            return;
        }

        _p2p.LocalScan.ClientsChanged += OnPeerLanPresenceChanged;
        var uiSync = SynchronizationContext.Current;
        _p2pSession = _p2p.GetOrCreateSession(chat, user, _auth, _repo, uiSync);
        _p2pSession.MessagesChanged += OnP2PMessagesChanged;
        if (!_p2p.IsChatSessionStarted(chat.Id))
        {
            try
            {
                await _p2pSession.StartAsync().ConfigureAwait(true);
                _p2p.MarkChatSessionStarted(chat.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not start UDP for chat {ChatId}", chat.Id);
                await DisplayAlert("P2P", $"Could not start UDP: {ex.Message}", "OK").ConfigureAwait(true);
            }
        }

        await ReloadMessagesAsync().ConfigureAwait(true);
        RefreshPeerPresenceLabel();
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        _p2p.LocalScan.ClientsChanged -= OnPeerLanPresenceChanged;
        _peerNetworkIdShort = null;
        if (_p2pSession != null)
        {
            _p2pSession.MessagesChanged -= OnP2PMessagesChanged;
            _p2pSession = null;
        }
    }

    private void OnPeerLanPresenceChanged(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(RefreshPeerPresenceLabel);

    private void OnP2PMessagesChanged(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(async () => await ReloadMessagesAsync().ConfigureAwait(true));

    private async Task ReloadMessagesAsync()
    {
        var rows = await _repo.ListMessagesAsync(ChatId).ConfigureAwait(true);
        MessagesCollection.ItemsSource = rows
            .Select(m =>
            {
                var sender = m.Outgoing ? "You" : Title ?? "Peer";
                var color = m.Outgoing ? Colors.DodgerBlue : GetPaletteColor(sender);
                var sentLocal = new DateTimeOffset(m.SentUtcTicks, TimeSpan.Zero).ToLocalTime();
                var text =
                    $"[{sentLocal.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture)}] {m.Text}";
                return new MessageRow(text, color);
            })
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
            _logger.LogWarning(ex, "Send message failed");
            await DisplayAlert("Send failed", ex.Message, "OK").ConfigureAwait(true);
        }
    }

    private sealed record MessageRow(string Text, Color MessageColor);

    private static Color GetPaletteColor(string key)
    {
        var hash = Math.Abs(key.GetHashCode(StringComparison.Ordinal));
        var idx = hash % 64;
        var hue = idx * (360.0f / 64.0f);
        // palette of 64 readable non-background colors
        return Color.FromHsla(hue / 360.0f, 0.72f, 0.44f);
    }

    private void RefreshPeerPresenceLabel()
    {
        if (string.IsNullOrWhiteSpace(_peerNetworkIdShort))
            return;

        var online = _p2p.LocalScan.IsPeerSeenRecentlyOnLan(_peerNetworkIdShort);
        PeerPresenceDot.Fill = online ? PresenceOnline : PresenceOffline;
        PeerStatusLabel.Text = online ? "Статус: онлайн" : "Статус: офлайн";
        PeerStatusLabel.TextColor = online ? PresenceOnline : Colors.Gray;
    }
}
