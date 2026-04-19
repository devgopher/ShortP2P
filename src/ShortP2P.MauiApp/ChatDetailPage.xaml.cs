using System.Globalization;
using Microsoft.Extensions.Logging;
using ShortP2P.Client;
using ShortP2P.Client.ChatMedia;
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
    private readonly ChatMediaOptions _media;
    private readonly ILogger<ChatDetailPage> _logger;
    private ChatP2pSession? _p2pSession;
    private string? _peerNetworkIdShort;

    public ChatDetailPage(AuthService auth, ChatRepository repo, UserP2pRuntime p2p, ChatMediaOptions media,
        ILogger<ChatDetailPage> logger)
    {
        InitializeComponent();
        _auth = auth;
        _repo = repo;
        _p2p = p2p;
        _media = media;
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

    protected override void OnDisappearing()
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
        var rows = (await _repo.ListMessagesAsync(ChatId).ConfigureAwait(true))
            .OrderByDescending(m => m.SentUtcTicks)
            .ThenByDescending(m => m.Id);
        var list = new List<MessageRowVm>();
        foreach (var m in rows)
        {
            var sender = m.Outgoing ? "You" : Title ?? "Peer";
            var color = m.Outgoing ? Colors.DodgerBlue : GetPaletteColor(sender);
            var sentLocal = new DateTimeOffset(m.SentUtcTicks, TimeSpan.Zero).ToLocalTime();
            var ts = sentLocal.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture);
            var ds = (MessageDeliveryStatus)m.DeliveryStatus;
            if (m.Outgoing && ds == MessageDeliveryStatus.NotApplicable)
                ds = MessageDeliveryStatus.Delivered;
            var (glyph, gColor, show) = DeliveryUiFor(ds, m.Outgoing);

            if (m.PayloadKind == (int)ChatPayloadKind.Image && m.ImageBlob is { Length: > 0 } blob)
            {
                var kb = (blob.Length + 1023) / 1024;
                var mimeShort = string.IsNullOrEmpty(m.MimeType) ? "image" : m.MimeType.Replace("image/", "");
                list.Add(new MessageRowVm
                {
                    CaptionLine = $"[{ts}] {sender} · {mimeShort} · {kb} КБ",
                    TextBody = "",
                    ShowTextBody = false,
                    IsImage = true,
                    ImagePreview = ImageSource.FromStream(() => new MemoryStream(blob)),
                    MessageColor = color,
                    ShowDelivery = show,
                    DeliveryGlyph = glyph,
                    DeliveryGlyphColor = gColor,
                });
            }
            else
            {
                list.Add(new MessageRowVm
                {
                    CaptionLine = $"[{ts}] {sender}",
                    TextBody = m.Text,
                    ShowTextBody = true,
                    IsImage = false,
                    ImagePreview = null,
                    MessageColor = color,
                    ShowDelivery = show,
                    DeliveryGlyph = glyph,
                    DeliveryGlyphColor = gColor,
                });
            }
        }

        MessagesCollection.ItemsSource = list;
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
        }
        catch (OutboundMessageQueuedException ex)
        {
            _logger.LogInformation(ex, "Message queued until peer is on LAN");
            await DisplayAlert("Ожидание сети", ex.Message, "OK").ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Send message failed");
            await DisplayAlert("Send failed", ex.Message, "OK").ConfigureAwait(true);
        }
        finally
        {
            await ReloadMessagesAsync().ConfigureAwait(true);
        }
    }

    private async void OnAttachImageClicked(object? sender, EventArgs e)
    {
        if (_p2pSession == null)
            return;

        try
        {
            var pick = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Изображение (JPEG, PNG, GIF)",
                FileTypes = FilePickerFileType.Images,
            }).ConfigureAwait(true);
            if (pick == null)
                return;

            if (!ImageAttachHelper.TryGetMimeFromExtension(pick.FileName, out var mime))
            {
                await DisplayAlert("Файл", "Допустимы только .jpg, .jpeg, .png, .gif", "OK").ConfigureAwait(true);
                return;
            }

            await using var stream = await pick.OpenReadAsync().ConfigureAwait(true);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms).ConfigureAwait(true);
            var bytes = ms.ToArray();
            if (bytes.Length < 12)
            {
                await DisplayAlert("Файл", "Файл слишком маленький.", "OK").ConfigureAwait(true);
                return;
            }

            if (!ImageAttachHelper.SniffMatchesMime(bytes.AsSpan(0, Math.Min(12, bytes.Length)), mime))
            {
                await DisplayAlert("Файл", "Содержимое не совпадает с расширением файла.", "OK").ConfigureAwait(true);
                return;
            }

            if (bytes.Length > _media.MaxImageBytes)
            {
                var limKb = (_media.MaxImageBytes + 1023) / 1024;
                var want = await DisplayAlert("Размер",
                    $"Файл {(bytes.Length + 1023) / 1024} КБ больше лимита {limKb} КБ (настраивается в chat-media.json). Сжать изображение?",
                    "Сжать",
                    "Отмена").ConfigureAwait(true);
                if (!want)
                    return;
                if (!ImageAttachmentCompressor.TryCompressToMaxBytes(bytes, _media.MaxImageBytes, out var compressed,
                        out var err))
                {
                    await DisplayAlert("Сжатие", err ?? "Не удалось уложиться в лимит.", "OK").ConfigureAwait(true);
                    return;
                }

                bytes = compressed;
                mime = ImageAttachmentCompressor.SuggestMimeAfterCompression();
            }

            _media.ValidateMime(mime);
            await _p2pSession.SendImageAsync(bytes, mime).ConfigureAwait(true);
        }
        catch (OutboundMessageQueuedException ex)
        {
            _logger.LogInformation(ex, "Image queued until peer is on LAN");
            await DisplayAlert("Ожидание сети", ex.Message, "OK").ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Send image failed");
            await DisplayAlert("Изображение", ex.Message, "OK").ConfigureAwait(true);
        }
        finally
        {
            await ReloadMessagesAsync().ConfigureAwait(true);
        }
    }

    private static (string Glyph, Color GlyphColor, bool Show) DeliveryUiFor(MessageDeliveryStatus status,
        bool outgoing)
    {
        if (!outgoing)
            return ("", Colors.Transparent, false);
        return status switch
        {
            MessageDeliveryStatus.Pending => (OutgoingDeliveryIndicators.Pending, Color.FromArgb("#B8860B"), true),
            MessageDeliveryStatus.Delivered => (OutgoingDeliveryIndicators.Delivered, Color.FromArgb("#228B22"), true),
            MessageDeliveryStatus.Failed => (OutgoingDeliveryIndicators.Failed, Colors.Red, true),
            _ => (OutgoingDeliveryIndicators.Delivered, Color.FromArgb("#228B22"), true),
        };
    }

    private static Color GetPaletteColor(string key)
    {
        var hash = Math.Abs(key.GetHashCode(StringComparison.Ordinal));
        var idx = hash % 64;
        var hue = idx * (360.0f / 64.0f);
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
