using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Devices;
using ShortP2P.Client;
using ShortP2P.Client.ChatMedia;
using ShortP2P.Client.Data;
using ShortP2P.Client.Services;

namespace ShortP2P.MauiApp;

public partial class ChatDetailPage : ContentPage
{
    private static readonly Color PresenceOnline = Color.FromArgb("#228B22");
    private static readonly Color PresenceOffline = Color.FromArgb("#CD5C5C");

    private static readonly FilePickerFileType OfficeDocFileTypes = new(new Dictionary<DevicePlatform, IEnumerable<string>>
    {
        [DevicePlatform.WinUI] =
        [
            ".doc", ".docx", ".rtf", ".pdf", ".odt", ".ods", ".odp", ".odg", ".xlsx", ".xls", ".pptx", ".ppt",
        ],
        [DevicePlatform.Android] =
        [
            "application/pdf",
            "application/msword",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "application/vnd.ms-excel",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "application/vnd.ms-powerpoint",
            "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            "application/vnd.oasis.opendocument.text",
            "application/vnd.oasis.opendocument.spreadsheet",
            "application/vnd.oasis.opendocument.presentation",
            "application/vnd.oasis.opendocument.graphics",
            "application/rtf",
        ],
        [DevicePlatform.iOS] = ["public.data"],
        [DevicePlatform.MacCatalyst] = ["public.data"],
    });

    private readonly AuthService _auth;
    private readonly ChatRepository _repo;
    private readonly UserP2pRuntime _p2p;
    private readonly ChatMediaOptions _media;
    private readonly ILogger<ChatDetailPage> _logger;
    private ChatP2pSession? _p2pSession;
    private string? _peerNetworkIdShort;
    private IDispatcherTimer? _presenceRefreshTimer;

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

        try
        {
            await _p2p.EnsureStartedAsync(user).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ensure P2P (invite listener) on chat detail");
        }

        _p2p.LocalScan.ClientsChanged += OnPeerLanPresenceChanged;
        EnsurePresenceRefreshTimerStarted();
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
        if (_presenceRefreshTimer != null)
            _presenceRefreshTimer.Stop();
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

    private void EnsurePresenceRefreshTimerStarted()
    {
        _presenceRefreshTimer ??= Dispatcher.CreateTimer();
        _presenceRefreshTimer.Interval = TimeSpan.FromSeconds(2);
        _presenceRefreshTimer.Tick -= OnPresenceRefreshTimerTick;
        _presenceRefreshTimer.Tick += OnPresenceRefreshTimerTick;
        if (!_presenceRefreshTimer.IsRunning)
            _presenceRefreshTimer.Start();
    }

    private void OnPresenceRefreshTimerTick(object? sender, EventArgs e) => RefreshPeerPresenceLabel();

    private async Task ReloadMessagesAsync()
    {
        var rows = (await _repo.ListMessagesAsync(ChatId).ConfigureAwait(true))
            .OrderByDescending(m => m.SentUtcTicks)
            .ThenByDescending(m => m.Id);
        var list = new List<MessageRowVm>();
        foreach (var m in rows)
        {
            var sender = m.Outgoing ? "You" : Title ?? "Peer";
            var peerNick = Title ?? "Peer";
            var whoPrefix = m.Outgoing ? "[Я:]" : $"[{peerNick}:]";
            var color = m.Outgoing ? Colors.DodgerBlue : GetPaletteColor(sender);
            var sentLocal = new DateTimeOffset(m.SentUtcTicks, TimeSpan.Zero).ToLocalTime();
            var ts = sentLocal.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture);
            var ds = (MessageDeliveryStatus)m.DeliveryStatus;
            if (m.Outgoing && ds == MessageDeliveryStatus.NotApplicable)
                ds = MessageDeliveryStatus.Delivered;
            var (glyph, gColor, show) = DeliveryUiFor(ds, m.Outgoing);

            if (m.PayloadKind == (int)ChatPayloadKind.File && m.ImageBlob is { Length: > 0 } docBlob)
            {
                var kb = (docBlob.Length + 1023) / 1024;
                var name = string.IsNullOrEmpty(m.Text) ? "file" : m.Text;
                var kindCaption = m.MimeType?.StartsWith("video/", StringComparison.OrdinalIgnoreCase) == true
                    ? "видео"
                    : "документ";
                var fileBody = new FormattedString();
                fileBody.Spans.Add(new Span
                {
                    Text = $"{name} · {kb} КБ · нажмите строку — ",
                    TextColor = color,
                });
                fileBody.Spans.Add(new Span { Text = "Скачать", TextColor = Colors.DodgerBlue });
                list.Add(new MessageRowVm
                {
                    CaptionLine = $"{whoPrefix} [{ts}] · {kindCaption}",
                    TextBody = "",
                    FileBodyFormatted = fileBody,
                    ShowTextBody = false,
                    IsImage = false,
                    IsFile = true,
                    MessageId = m.Id,
                    ImagePreview = null,
                    MessageColor = color,
                    ShowDelivery = show,
                    DeliveryGlyph = glyph,
                    DeliveryGlyphColor = gColor,
                });
            }
            else if (m.PayloadKind == (int)ChatPayloadKind.Image && m.ImageBlob is { Length: > 0 } blob)
            {
                var kb = (blob.Length + 1023) / 1024;
                var mimeShort = string.IsNullOrEmpty(m.MimeType) ? "image" : m.MimeType.Replace("image/", "");
                list.Add(new MessageRowVm
                {
                    CaptionLine = $"{whoPrefix} [{ts}] · {mimeShort} · {kb} КБ",
                    TextBody = "",
                    ShowTextBody = false,
                    IsImage = true,
                    IsFile = false,
                    MessageId = 0,
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
                    CaptionLine = $"{whoPrefix} [{ts}]",
                    TextBody = m.Text,
                    ShowTextBody = true,
                    IsImage = false,
                    IsFile = false,
                    MessageId = 0,
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

    private async void OnAttachDocumentClicked(object? sender, EventArgs e)
    {
        if (_p2pSession == null)
            return;

        try
        {
            var pick = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Документ Word / LibreOffice (до 10 МБ)",
                FileTypes = OfficeDocFileTypes,
            }).ConfigureAwait(true);
            if (pick == null)
                return;

            if (!DocumentAttachHelper.TryGetMimeFromExtension(pick.FileName, out var mime))
            {
                await DisplayAlert("Файл",
                        "Допустимы только .doc, .docx, .rtf, .pdf, .odt, .ods, .odp, .odg, .xlsx, .xls, .pptx, .ppt.",
                        "OK")
                    .ConfigureAwait(true);
                return;
            }

            await using var stream = await pick.OpenReadAsync().ConfigureAwait(true);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms).ConfigureAwait(true);
            var bytes = ms.ToArray();
            if (bytes.Length == 0)
            {
                await DisplayAlert("Файл", "Файл пустой.", "OK").ConfigureAwait(true);
                return;
            }

            var headLen = Math.Min(4096, bytes.Length);
            if (!DocumentAttachHelper.SniffMatchesMime(bytes.AsSpan(0, headLen), mime))
            {
                await DisplayAlert("Файл", "Содержимое не совпадает с типом файла.", "OK").ConfigureAwait(true);
                return;
            }

            if (bytes.Length > _media.MaxDocumentBytes)
            {
                var limMb = (_media.MaxDocumentBytes + (1024 * 1024 - 1)) / (1024 * 1024);
                await DisplayAlert("Размер", $"Файл больше {limMb} МБ (лимит maxDocumentBytes в chat-media.json).", "OK")
                    .ConfigureAwait(true);
                return;
            }

            _media.ValidateDocumentMime(mime);
            await _p2pSession.SendFileAsync(pick.FileName, bytes, mime).ConfigureAwait(true);
        }
        catch (OutboundMessageQueuedException ex)
        {
            _logger.LogInformation(ex, "Document queued until peer is on LAN");
            await DisplayAlert("Ожидание сети", ex.Message, "OK").ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Send document failed");
            await DisplayAlert("Документ", ex.Message, "OK").ConfigureAwait(true);
        }
        finally
        {
            await ReloadMessagesAsync().ConfigureAwait(true);
        }
    }

    private async void OnMessageRowTapped(object? sender, TappedEventArgs e)
    {
        var walk = sender switch
        {
            TapGestureRecognizer tg => tg.Parent as Element,
            Element el => el,
            _ => null,
        };
        MessageRowVm? vm = null;
        for (var el = walk; el != null; el = el.Parent as Element)
        {
            if (el.BindingContext is MessageRowVm row)
            {
                vm = row;
                break;
            }
        }

        if (vm == null || !vm.IsFile || vm.MessageId == 0)
            return;

        try
        {
            var row = await _repo.GetMessageAsync(vm.MessageId).ConfigureAwait(true);
            if (row?.ImageBlob is not { Length: > 0 } blob)
            {
                await DisplayAlert("Файл", "Сообщение не найдено или пустое.", "OK").ConfigureAwait(true);
                return;
            }

            var name = SanitizeFileName(string.IsNullOrEmpty(row.Text) ? "document" : row.Text);
            var temp = Path.Combine(FileSystem.CacheDirectory, $"{vm.MessageId}_{name}");
            await File.WriteAllBytesAsync(temp, blob).ConfigureAwait(true);
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Сохранить или отправить документ",
                File = new ShareFile(temp),
            }).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Share document failed");
            await DisplayAlert("Файл", ex.Message, "OK").ConfigureAwait(true);
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (invalid.Contains(chars[i]))
                chars[i] = '_';
        }

        var s = new string(chars).Trim();
        return string.IsNullOrEmpty(s) ? "document" : s;
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
        const int hueSteps = 12;
        const int lightSteps = 3;
        var h = hash % hueSteps;
        var lBand = (hash / hueSteps) % lightSteps;
        var hueDeg = h * (360.0f / hueSteps);
        var lightness = 0.40f + lBand * 0.06f;
        return Color.FromHsla(hueDeg / 360.0f, 0.72f, lightness);
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
