using System.Net;
using Microsoft.Extensions.Logging;
using ShortP2P.Client.Data;
using ShortP2P.Client.Services;

namespace ShortP2P.MauiApp;

public partial class ChatDetailPage : ContentPage
{
    private readonly AuthService _auth;
    private readonly ChatRepository _repo;
    private readonly UserP2pRuntime _p2p;
    private readonly ILogger<ChatDetailPage> _logger;
    private ChatP2pSession? _p2pSession;

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
        PeerStatusLabel.Text = "Статус: офлайн";
        PeerHostEntry.Text = chat.PeerHost;
        PeerPortEntry.Text = chat.PeerPort.ToString();
        var user = _auth.CurrentUser;
        if (user == null)
        {
            await Navigation.PopAsync().ConfigureAwait(true);
            return;
        }

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
        if (_p2pSession != null)
        {
            _p2pSession.MessagesChanged -= OnP2PMessagesChanged;
            _p2pSession = null;
        }
    }

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
                return new MessageRow(sender, m.Text, color);
            })
            .ToList();
    }

    private async void OnApplyPeerEndpointClicked(object? sender, EventArgs e)
    {
        if (_p2pSession == null)
        {
            await DisplayAlert("Чат", "Сессия ещё не готова.", "OK").ConfigureAwait(true);
            return;
        }

        var host = PeerHostEntry.Text?.Trim() ?? "";
        if (host.Length == 0)
        {
            await DisplayAlert("Адрес", "Укажите IP или hostname.", "OK").ConfigureAwait(true);
            return;
        }

        if (!int.TryParse(PeerPortEntry.Text?.Trim(), out var port) || port is < 1 or > 65535)
        {
            await DisplayAlert("Адрес", "Порт должен быть числом 1–65535.", "OK").ConfigureAwait(true);
            return;
        }

        try
        {
            _ = IPAddress.Parse(host);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invalid IP for peer endpoint");
            await DisplayAlert("Адрес", "Некорректный IP или hostname.", "OK").ConfigureAwait(true);
            return;
        }

        try
        {
            await _p2pSession.ApplyPeerEndpointAsync(host, port).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Apply peer endpoint failed");
            await DisplayAlert("Адрес", ex.Message, "OK").ConfigureAwait(true);
        }
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

    private sealed record MessageRow(string DirectionLabel, string Text, Color MessageColor);

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
        PeerStatusLabel.Text = "Статус: офлайн";
        PeerStatusLabel.TextColor = Colors.Gray;
    }
}
