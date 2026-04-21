using System.Drawing.Drawing2D;
using System.Globalization;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ShortP2P.Client;
using ShortP2P.Client.ChatMedia;
using ShortP2P.Client.Data;
using ShortP2P.Client.Services;
using ShortP2P.Transport.Bluetooth.Windows;

namespace ShortP2P.WinForms;

public sealed class ChatForm : Form
{
    private const string FileCaptionNewline = "\r\n";
    private const string FileDownloadHintPrefix = "Двойной щелчок — ";
    private const string FileDownloadHintAction = "Скачать";
    private static readonly Color FileDownloadActionColor = Color.FromArgb(0, 102, 204);

    private readonly ChatEntity _chat;
    private readonly UserEntity _user;
    private readonly AuthService _auth;
    private readonly ChatRepository _repo;
    private readonly UserP2pRuntime _p2PRuntime;
    private readonly ChatMediaOptions _media;
    private readonly ILogger<ChatForm> _logger;
    private readonly ILogger<UserAction> _userActions;
    private readonly Label _peerInfoLabel = new()
    {
        AutoSize = true,
        ForeColor = SystemColors.GrayText,
        Padding = new Padding(0, 0, 0, 2),
    };
    private readonly ListBox _messages = new()
    {
        Dock = DockStyle.Fill,
        IntegralHeight = false,
        ScrollAlwaysVisible = true,
        Padding = new Padding(8, 5, 8, 4),
        DrawMode = DrawMode.OwnerDrawVariable,
    };
    private readonly RichTextBox _input = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        AcceptsTab = false,
        BorderStyle = BorderStyle.FixedSingle,
        MaxLength = ChatP2pSession.MaxMessageChars,
    };
    private readonly Button _attachImage = new()
    {
        Text = "🖼",
        Dock = DockStyle.Right,
        Width = 36,
        Height = 36,
        Font = new Font("Segoe UI Emoji", 10, FontStyle.Regular, GraphicsUnit.Point),
    };
    private readonly Button _attachVideo = new()
    {
        Text = "🎬",
        Dock = DockStyle.Right,
        Width = 36,
        Height = 36,
        Font = new Font("Segoe UI Emoji", 10, FontStyle.Regular, GraphicsUnit.Point),
    };
    private readonly Button _attachDocument = new()
    {
        Text = "📄",
        Dock = DockStyle.Right,
        Width = 36,
        Height = 36,
        Font = new Font("Segoe UI Emoji", 10, FontStyle.Regular, GraphicsUnit.Point),
    };
    private readonly Button _send = new()
    {
        Text = "➤",
        Dock = DockStyle.Right,
        Width = 36,
        Height = 36,
        Font = new Font("Segoe UI", 10, FontStyle.Bold, GraphicsUnit.Point),
    };
    private readonly ToolTip _buttonTooltips = new() { ShowAlways = true };
    private ChatP2pSession? _p2PSession;
    private bool _pairingPromptShown;

    public ChatForm(ChatEntity chat, UserEntity user, AuthService auth, ChatRepository repo, UserP2pRuntime p2PRuntime,
        ILogger<ChatForm> logger, ILogger<UserAction> userActions, ChatMediaOptions media)
    {
        _chat = chat;
        _user = user;
        _auth = auth;
        _repo = repo;
        _p2PRuntime = p2PRuntime;
        _media = media;
        _logger = logger;
        _userActions = userActions;
        Text = chat.PeerNickname;
        StartPosition = FormStartPosition.CenterParent;
        Width = 520;
        Height = 520;
        MaximizeBox = false;

        _peerInfoLabel.Text = PeerInfoText("Статус: офлайн");

        var top = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(8, 6, 8, 2),
            ColumnCount = 1,
            RowCount = 1,
        };
        top.Controls.Add(_peerInfoLabel, 0, 0);

        var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 76, ColumnCount = 5 };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.Controls.Add(_input, 0, 0);
        bottom.Controls.Add(_attachImage, 1, 0);
        bottom.Controls.Add(_attachVideo, 2, 0);
        bottom.Controls.Add(_attachDocument, 3, 0);
        bottom.Controls.Add(_send, 4, 0);

        _buttonTooltips.SetToolTip(_attachImage, "Отправить изображение");
        _buttonTooltips.SetToolTip(_attachVideo, "Отправить видео");
        _buttonTooltips.SetToolTip(_attachDocument, "Отправить документ");
        _buttonTooltips.SetToolTip(_send, "Отправить сообщение");

        // Порядок: Fill сначала, затем Top/Bottom — иначе между шапкой и вводом остаётся пустая полоса.
        Controls.Add(_messages);
        Controls.Add(top);
        Controls.Add(bottom);

        _attachImage.Click += async (_, _) => await OnAttachImageAsync().ConfigureAwait(true);
        _attachVideo.Click += async (_, _) => await OnAttachVideoAsync().ConfigureAwait(true);
        _attachDocument.Click += async (_, _) => await OnAttachDocumentAsync().ConfigureAwait(true);
        _send.Click += async (_, _) => await OnSendAsync().ConfigureAwait(true);
        _messages.DrawItem += OnMessagesDrawItem;
        _messages.MeasureItem += OnMessagesMeasureItem;
        _messages.DoubleClick += OnMessageDoubleClick;
        Shown += async (_, _) => await OnShownAsync().ConfigureAwait(true);
    }

    private async Task OnShownAsync()
    {
        _userActions.LogInformation("Chat {Peer}: window opened (chat id {ChatId})", _chat.PeerNickname, _chat.Id);
        var uiSync = SynchronizationContext.Current;
        var fresh = await _repo.GetChatAsync(_chat.Id).ConfigureAwait(true) ?? _chat;
        _p2PSession = _p2PRuntime.GetOrCreateSession(fresh, _user, _auth, _repo, uiSync);
        _p2PSession.MessagesChanged += OnP2pMessagesChanged;
        if (!_p2PRuntime.IsChatSessionStarted(_chat.Id))
        {
            try
            {
                await _p2PSession.StartAsync().ConfigureAwait(true);
                _p2PRuntime.MarkChatSessionStarted(_chat.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not start UDP for chat {ChatId}", _chat.Id);
                if (HandleBluetoothUnavailable(ex))
                    return;
                MessageBox.Show(this, $"Could not start UDP: {ex.Message}", "P2P", MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        await ReloadMessagesAsync().ConfigureAwait(true);
        RefreshPeerPresenceLabel();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (_p2PSession != null)
        {
            _p2PSession.MessagesChanged -= OnP2pMessagesChanged;
            _p2PSession = null;
        }

        foreach (var item in _messages.Items)
        {
            if (item is ChatLine line)
                line.Dispose();
        }

        _messages.Items.Clear();
        base.OnFormClosed(e);
    }

    private void OnP2pMessagesChanged(object? sender, EventArgs e) =>
        BeginInvoke(() => _ = ReloadMessagesAsync());

    private async Task ReloadMessagesAsync()
    {
        try
        {
            var rows = (await _repo.ListMessagesAsync(_chat.Id).ConfigureAwait(true)).OrderByDescending(m => m.SentUtcTicks).ToList();
            if (!IsHandleCreated || IsDisposed)
                return;
            _messages.BeginUpdate();
            foreach (var existing in _messages.Items)
            {
                if (existing is ChatLine oldLine)
                    oldLine.Dispose();
            }

            _messages.Items.Clear();
            foreach (var m in rows)
            {
                var sender = m.Outgoing ? "You" : _chat.PeerNickname;
                var whoPrefix = m.Outgoing ? "[Я:]" : $"[{_chat.PeerNickname}:]";
                var color = m.Outgoing ? Color.DodgerBlue : GetPaletteColor(sender);
                var sentLocal = new DateTimeOffset(m.SentUtcTicks, TimeSpan.Zero).ToLocalTime();
                var ts = sentLocal.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture);
                var ds = (MessageDeliveryStatus)m.DeliveryStatus;
                if (m.Outgoing && ds == MessageDeliveryStatus.NotApplicable)
                    ds = MessageDeliveryStatus.Delivered;

                if (m.PayloadKind == (int)ChatPayloadKind.File && m.ImageBlob is { Length: > 0 } fileBlob)
                {
                    var kb = (fileBlob.Length + 1023) / 1024;
                    var name = string.IsNullOrEmpty(m.Text) ? "file" : m.Text;
                    var kindCaption = m.MimeType?.StartsWith("video/", StringComparison.OrdinalIgnoreCase) == true
                        ? "видео"
                        : "документ";
                    var caption =
                        $"{whoPrefix} [{ts}] · {kindCaption} · {name} · {kb} КБ{FileCaptionNewline}{FileDownloadHintPrefix}{FileDownloadHintAction}";
                    _messages.Items.Add(new ChatLine(caption, color, m.Outgoing, ds, ChatLineKind.File, fileBlob, name));
                }
                else if (m.PayloadKind == (int)ChatPayloadKind.Image && m.ImageBlob is { Length: > 0 } blob)
                {
                    var kb = (blob.Length + 1023) / 1024;
                    var mimeShort = string.IsNullOrEmpty(m.MimeType) ? "image" : m.MimeType.Replace("image/", "");
                    var caption = $"{whoPrefix} [{ts}] · {mimeShort} · {kb} КБ";
                    _messages.Items.Add(new ChatLine(caption, color, m.Outgoing, ds, ChatLineKind.Image, blob, null));
                }
                else
                {
                    var full = $"{whoPrefix} [{ts}] {m.Text}";
                    _messages.Items.Add(new ChatLine(full, color, m.Outgoing, ds, ChatLineKind.Text, null, null));
                }
            }
            _messages.EndUpdate();
        }
        catch (ObjectDisposedException)
        {
            // expected while closing
        }
    }

    private async Task OnAttachVideoAsync()
    {
        if (_p2PSession == null)
            return;

        using var dlg = new OpenFileDialog
        {
            Title = "Видео (до 60 секунд, до 10 МБ)",
            Filter = "Видео|*.mp4;*.avi;*.mov;*.wmv;*.ogv;*.webm|Все файлы|*.*",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            if (!VideoAttachHelper.TryGetMimeFromExtension(dlg.FileName, out var mime))
            {
                MessageBox.Show(this, "Допустимы только .mp4, .avi, .mov, .wmv, .ogv, .webm.", "Видео", MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (!VideoAttachHelper.AreBundledToolsAvailable())
            {
                var wantDownload = MessageBox.Show(this,
                    "Для проверки длительности и сжатия видео нужны ffmpeg/ffprobe. Скачать их в подпапку ffmpeg?",
                    "Видео",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button1);
                if (wantDownload == DialogResult.Yes)
                {
                    UseWaitCursor = true;
                    try
                    {
                        var dl = await VideoAttachHelper.TryDownloadBundledToolsAsync().ConfigureAwait(true);
                        if (!dl.Success)
                        {
                            MessageBox.Show(this,
                                $"Не удалось скачать ffmpeg/ffprobe. Продолжаем без них.\r\n{dl.Error}",
                                "Видео",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Information);
                        }
                    }
                    finally
                    {
                        UseWaitCursor = false;
                    }
                }
            }

            var bytes = await File.ReadAllBytesAsync(dlg.FileName).ConfigureAwait(true);
            var durationInfo = await VideoAttachHelper.TryProbeDurationSecondsAsync(dlg.FileName).ConfigureAwait(true);
            if (durationInfo is { Success: true, DurationSeconds: > 60.0 })
            {
                MessageBox.Show(this, $"Видео длится {Math.Ceiling(durationInfo.DurationSeconds)} сек. Лимит: 60 сек.",
                    "Видео", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var outgoingName = Path.GetFileName(dlg.FileName);
            if (bytes.Length > _media.MaxDocumentBytes)
            {
                var limMb = (_media.MaxDocumentBytes + 1048575) / 1048576;
                if (!durationInfo.Success)
                {
                    MessageBox.Show(this,
                        $"Файл больше {limMb} МБ (лимит в chat-media.json: maxDocumentBytes).",
                        "Видео",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                var prepared = await VideoAttachHelper.TryCompressToLimitAsync(dlg.FileName, _media.MaxDocumentBytes)
                    .ConfigureAwait(true);
                if (!prepared.Success || prepared.Bytes == null || prepared.OutputFileName == null ||
                    prepared.OutputMime == null)
                {
                    MessageBox.Show(this,
                        prepared.Error ?? $"Не удалось уложить видео в {limMb} МБ после сжатия/понижения разрешения.",
                        "Видео",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                bytes = prepared.Bytes;
                outgoingName = prepared.OutputFileName;
                mime = prepared.OutputMime;
            }

            _media.ValidateDocumentMime(mime);
            _media.ValidateDocumentSize(bytes.Length);
            await _p2PSession.SendFileAsync(outgoingName, bytes, mime).ConfigureAwait(true);
            _userActions.LogInformation("Chat {Peer}: sent video ({Bytes} bytes, {Mime}, {Seconds} sec)",
                _chat.PeerNickname, bytes.Length, mime, durationInfo.Success ? durationInfo.DurationSeconds : -1.0);
        }
        catch (OutboundMessageQueuedException ex)
        {
            _logger.LogInformation(ex, "Video queued until peer is on LAN (chat {ChatId})", _chat.Id);
            _userActions.LogInformation("Chat {Peer}: video queued for LAN delivery", _chat.PeerNickname);
            MessageBox.Show(this, ex.Message, "Ожидание сети", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Send video failed in chat {ChatId}", _chat.Id);
            _userActions.LogInformation("Chat {Peer}: send video failed ({Message})", _chat.PeerNickname, ex.Message);
            if (HandleBluetoothUnavailable(ex))
                return;
            MessageBox.Show(this, ex.Message, "Видео", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            await ReloadMessagesAsync().ConfigureAwait(true);
        }
    }

    private async Task OnAttachImageAsync()
    {
        if (_p2PSession == null)
            return;

        using var dlg = new OpenFileDialog
        {
            Title = "Изображение (JPEG, PNG, GIF)",
            Filter = "Изображения|*.jpg;*.jpeg;*.png;*.gif|Все файлы|*.*",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            if (!ImageAttachHelper.TryGetMimeFromExtension(dlg.FileName, out var mime))
            {
                MessageBox.Show(this, "Допустимы только .jpg, .jpeg, .png, .gif.", "Файл", MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            var bytes = await File.ReadAllBytesAsync(dlg.FileName).ConfigureAwait(true);
            if (bytes.Length < 12)
            {
                MessageBox.Show(this, "Файл слишком маленький.", "Файл", MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (!ImageAttachHelper.SniffMatchesMime(bytes.AsSpan(0, Math.Min(12, bytes.Length)), mime))
            {
                MessageBox.Show(this, "Содержимое не совпадает с расширением файла.", "Файл", MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            if (bytes.Length > _media.MaxImageBytes)
            {
                var limKb = (_media.MaxImageBytes + 1023) / 1024;
                var want = MessageBox.Show(this,
                    $"Файл {(bytes.Length + 1023) / 1024} КБ больше лимита {limKb} КБ (настраивается в chat-media.json). Сжать изображение?",
                    "Размер",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button1);
                if (want != DialogResult.Yes)
                    return;
                if (!ImageAttachmentCompressor.TryCompressToMaxBytes(bytes, _media.MaxImageBytes, out var compressed,
                        out var err))
                {
                    MessageBox.Show(this, err ?? "Не удалось уложиться в лимит.", "Сжатие", MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                bytes = compressed;
                mime = ImageAttachmentCompressor.SuggestMimeAfterCompression();
            }

            _media.ValidateMime(mime);
            await _p2PSession.SendImageAsync(bytes, mime).ConfigureAwait(true);
            _userActions.LogInformation("Chat {Peer}: sent image ({Bytes} bytes, {Mime})", _chat.PeerNickname,
                bytes.Length, mime);
        }
        catch (OutboundMessageQueuedException ex)
        {
            _logger.LogInformation(ex, "Image queued until peer is on LAN (chat {ChatId})", _chat.Id);
            _userActions.LogInformation("Chat {Peer}: image queued for LAN delivery", _chat.PeerNickname);
            MessageBox.Show(this, ex.Message, "Ожидание сети", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Send image failed in chat {ChatId}", _chat.Id);
            _userActions.LogInformation("Chat {Peer}: send image failed ({Message})", _chat.PeerNickname, ex.Message);
            if (HandleBluetoothUnavailable(ex))
                return;
            MessageBox.Show(this, ex.Message, "Изображение", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            await ReloadMessagesAsync().ConfigureAwait(true);
        }
    }

    private async Task OnAttachDocumentAsync()
    {
        if (_p2PSession == null)
            return;

        using var dlg = new OpenFileDialog
        {
            Title = "Документ Word / LibreOffice (до 10 МБ)",
            Filter =
                "Документы|*.doc;*.docx;*.rtf;*.pdf;*.odt;*.ods;*.odp;*.odg;*.xlsx;*.xls;*.pptx;*.ppt|Все файлы|*.*",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            if (!DocumentAttachHelper.TryGetMimeFromExtension(dlg.FileName, out var mime))
            {
                MessageBox.Show(this,
                    "Допустимы только .doc, .docx, .rtf, .pdf, .odt, .ods, .odp, .odg, .xlsx, .xls, .pptx, .ppt.",
                    "Файл", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var bytes = await File.ReadAllBytesAsync(dlg.FileName).ConfigureAwait(true);
            if (bytes.Length == 0)
            {
                MessageBox.Show(this, "Файл пустой.", "Файл", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var headLen = Math.Min(4096, bytes.Length);
            if (!DocumentAttachHelper.SniffMatchesMime(bytes.AsSpan(0, headLen), mime))
            {
                MessageBox.Show(this, "Содержимое не совпадает с типом файла (ожидается корректный Office/LibreOffice).",
                    "Файл", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (bytes.Length > _media.MaxDocumentBytes)
            {
                var limMb = (_media.MaxDocumentBytes + (1024 * 1024 - 1)) / (1024 * 1024);
                MessageBox.Show(this, $"Файл больше {limMb} МБ (лимит в chat-media.json: maxDocumentBytes).", "Размер",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _media.ValidateDocumentMime(mime);
            await _p2PSession.SendFileAsync(dlg.FileName, bytes, mime).ConfigureAwait(true);
            _userActions.LogInformation("Chat {Peer}: sent document ({Bytes} bytes, {Mime})", _chat.PeerNickname,
                bytes.Length, mime);
        }
        catch (OutboundMessageQueuedException ex)
        {
            _logger.LogInformation(ex, "Document queued until peer is on LAN (chat {ChatId})", _chat.Id);
            _userActions.LogInformation("Chat {Peer}: document queued for LAN delivery", _chat.PeerNickname);
            MessageBox.Show(this, ex.Message, "Ожидание сети", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Send document failed in chat {ChatId}", _chat.Id);
            _userActions.LogInformation("Chat {Peer}: send document failed ({Message})", _chat.PeerNickname, ex.Message);
            if (HandleBluetoothUnavailable(ex))
                return;
            MessageBox.Show(this, ex.Message, "Документ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            await ReloadMessagesAsync().ConfigureAwait(true);
        }
    }

    private async Task OnSendAsync()
    {
        var text = _input.Text.Trim();
        if (text.Length == 0 || _p2PSession == null)
            return;
        try
        {
            await _p2PSession.SendTextAsync(text).ConfigureAwait(true);
            _userActions.LogInformation("Chat {Peer}: sent message ({Length} chars)", _chat.PeerNickname, text.Length);
            _input.Clear();
        }
        catch (OutboundMessageQueuedException ex)
        {
            _logger.LogInformation(ex, "Message queued until peer is on LAN (chat {ChatId})", _chat.Id);
            _userActions.LogInformation("Chat {Peer}: message queued for LAN delivery", _chat.PeerNickname);
            MessageBox.Show(this, ex.Message, "Ожидание сети", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Send message failed in chat {ChatId}", _chat.Id);
            _userActions.LogInformation("Chat {Peer}: send message failed ({Message})", _chat.PeerNickname, ex.Message);
            if (HandleBluetoothUnavailable(ex))
                return;
            MessageBox.Show(this, ex.Message, "Send failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            await ReloadMessagesAsync().ConfigureAwait(true);
        }
    }

    private void RefreshPeerPresenceLabel()
    {
        _peerInfoLabel.Text = PeerInfoText("Статус: офлайн");
        _peerInfoLabel.ForeColor = SystemColors.GrayText;
    }

    private string PeerInfoText(string statusLine) => $"Id: {_chat.PeerNetworkIdShort}\r\n{statusLine}";

    private bool HandleBluetoothUnavailable(Exception ex)
    {
        if (!WindowsBluetoothTransport.IsUnavailableError(ex))
            return false;

        _userActions.LogInformation("Chat {Peer}: bluetooth unavailable ({Message})", _chat.PeerNickname, ex.Message);
        if (!_p2PRuntime.Settings.SuggestBluetoothPairing || _pairingPromptShown)
            return true;

        _pairingPromptShown = true;
        var answer = MessageBox.Show(this,
            "Bluetooth-пир недоступен или не сопряжён. Открыть системные настройки Bluetooth для сопряжения?",
            "Bluetooth",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button1);
        if (answer != DialogResult.Yes)
            return true;

        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:bluetooth") { UseShellExecute = true });
            _userActions.LogInformation("Chat {Peer}: opened system bluetooth settings", _chat.PeerNickname);
        }
        catch (Exception openEx)
        {
            _logger.LogWarning(openEx, "Could not open Bluetooth settings");
        }

        return true;
    }

    private void OnMessageDoubleClick(object? sender, EventArgs e)
    {
        if (_messages.SelectedItem is not ChatLine line)
            return;
        if (line.Kind == ChatLineKind.File && line.PayloadBytes is { Length: > 0 })
        {
            using var sfd = new SaveFileDialog
            {
                Title = "Сохранить файл",
                FileName = string.IsNullOrEmpty(line.FileSuggestedName) ? "document" : line.FileSuggestedName,
                Filter = "Все файлы|*.*",
                OverwritePrompt = true,
            };
            if (sfd.ShowDialog(this) == DialogResult.OK)
            {
                _userActions.LogInformation("Chat {Peer}: save document to {Path}", _chat.PeerNickname, sfd.FileName);
                File.WriteAllBytes(sfd.FileName, line.PayloadBytes);
            }

            return;
        }

        if (line.Kind == ChatLineKind.Image && line.PayloadBytes is { Length: > 0 })
        {
            _userActions.LogInformation("Chat {Peer}: open image viewer", _chat.PeerNickname);
            using var v = new ImageViewForm("Изображение", line.PayloadBytes);
            v.ShowDialog(this);
            return;
        }

        if (line.DisplayText.Length <= 64)
            return;
        _userActions.LogInformation("Chat {Peer}: open long message viewer", _chat.PeerNickname);
        using var dlg = new MessageViewForm("Сообщение", line.DisplayText);
        dlg.ShowDialog(this);
    }

    private void OnMessagesDrawItem(object? sender, DrawItemEventArgs e)
    {
        e.DrawBackground();
        if (e.Index < 0 || e.Index >= _messages.Items.Count)
            return;

        var line = _messages.Items[e.Index] as ChatLine;
        var text = line?.DisplayText ?? _messages.Items[e.Index]?.ToString() ?? "";
        var color = line?.Color ?? ForeColor;
        const int statusCol = 22;
        var reserveRight = line is { Outgoing: true } ? statusCol : 0;
        var font = e.Font ?? _messages.Font;
        var textWidth = Math.Max(10, e.Bounds.Width - 8 - reserveRight);
        var captionMeasured = TextRenderer.MeasureText(text, font, new Size(textWidth, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        var captionH = captionMeasured.Height;
        Rectangle textBounds;

        if (line?.Kind == ChatLineKind.File &&
            text.IndexOf(FileCaptionNewline, StringComparison.Ordinal) is var nlIdx && nlIdx >= 0)
        {
            var head = text[..nlIdx];
            var headMeasured = TextRenderer.MeasureText(head, font, new Size(textWidth, int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            textBounds = new Rectangle(e.Bounds.X + 4, e.Bounds.Y + 2, textWidth,
                Math.Max(font.Height, headMeasured.Height));
            TextRenderer.DrawText(e.Graphics, head, font, textBounds, color,
                TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);

            var line2Y = textBounds.Bottom + 2;
            var prefixW = TextRenderer.MeasureText(FileDownloadHintPrefix, font).Width;
            var prefixBounds = new Rectangle(e.Bounds.X + 4, line2Y, prefixW + 2, font.Height + 2);
            TextRenderer.DrawText(e.Graphics, FileDownloadHintPrefix, font, prefixBounds, color,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                TextFormatFlags.SingleLine);

            var actionBounds = new Rectangle(e.Bounds.X + 4 + prefixW, line2Y,
                Math.Max(10, textWidth - prefixW), font.Height + 2);
            TextRenderer.DrawText(e.Graphics, FileDownloadHintAction, font, actionBounds, FileDownloadActionColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                TextFormatFlags.SingleLine);
        }
        else
        {
            textBounds = new Rectangle(e.Bounds.X + 4, e.Bounds.Y + 2, textWidth, Math.Max(font.Height, captionH));
            TextRenderer.DrawText(e.Graphics, text, font, textBounds, color,
                TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        }

        if (line is { Kind: ChatLineKind.Image, Thumbnail: { } thumb })
        {
            var imgY = textBounds.Bottom + 4;
            var imgRect = new Rectangle(e.Bounds.X + 4, imgY, thumb.Width, thumb.Height);
            e.Graphics.DrawImage(thumb, imgRect);
        }

        if (line is { Outgoing: true })
        {
            var (glyph, gColor) = OutgoingDeliveryDraw(line.DeliveryStatus);
            var gb = new Rectangle(e.Bounds.Right - statusCol - 2, e.Bounds.Y + 2, statusCol,
                Math.Max(font.Height, e.Bounds.Height - 4));
            TextRenderer.DrawText(e.Graphics, glyph, font, gb, gColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        e.DrawFocusRectangle();
    }

    private void OnMessagesMeasureItem(object? sender, MeasureItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _messages.Items.Count)
        {
            e.ItemHeight = Font.Height + 8;
            return;
        }

        var line = _messages.Items[e.Index] as ChatLine;
        var text = line?.DisplayText ?? _messages.Items[e.Index]?.ToString() ?? "";
        const int statusCol = 22;
        var reserveRight = line is { Outgoing: true } ? statusCol : 0;
        var width = Math.Max(120,
            _messages.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 12 - reserveRight);
        var measured = TextRenderer.MeasureText(text, _messages.Font, new Size(width, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        var h = measured.Height + 8;
        if (line is { Kind: ChatLineKind.Image, Thumbnail: { } thumb })
            h += 4 + thumb.Height + 4;
        e.ItemHeight = Math.Max(_messages.Font.Height + 8, h);
    }

    private static (string Glyph, Color Color) OutgoingDeliveryDraw(MessageDeliveryStatus status) =>
        status switch
        {
            MessageDeliveryStatus.Pending => (OutgoingDeliveryIndicators.Pending, Color.DarkGoldenrod),
            MessageDeliveryStatus.Delivered => (OutgoingDeliveryIndicators.Delivered, Color.ForestGreen),
            MessageDeliveryStatus.Failed => (OutgoingDeliveryIndicators.Failed, Color.Red),
            _ => (OutgoingDeliveryIndicators.Delivered, Color.ForestGreen),
        };

    private static Color GetPaletteColor(string key)
    {
        var hash = Math.Abs(StringComparer.Ordinal.GetHashCode(key));
        const int hueSteps = 12;
        const int lightSteps = 3;
        var h = hash % hueSteps;
        var lBand = (hash / hueSteps) % lightSteps;
        var hue = h * (360.0 / hueSteps);
        var lightness = 0.40 + lBand * 0.06;
        return ColorFromHsl(hue, 0.72, lightness);
    }

    private static Color ColorFromHsl(double hue, double saturation, double lightness)
    {
        hue = hue % 360.0;
        var c = (1.0 - Math.Abs(2.0 * lightness - 1.0)) * saturation;
        var x = c * (1.0 - Math.Abs((hue / 60.0) % 2.0 - 1.0));
        var m = lightness - c / 2.0;
        double r1, g1, b1;
        if (hue < 60) (r1, g1, b1) = (c, x, 0);
        else if (hue < 120) (r1, g1, b1) = (x, c, 0);
        else if (hue < 180) (r1, g1, b1) = (0, c, x);
        else if (hue < 240) (r1, g1, b1) = (0, x, c);
        else if (hue < 300) (r1, g1, b1) = (x, 0, c);
        else (r1, g1, b1) = (c, 0, x);

        var r = (int)Math.Round((r1 + m) * 255.0);
        var g = (int)Math.Round((g1 + m) * 255.0);
        var b = (int)Math.Round((b1 + m) * 255.0);
        return Color.FromArgb(r, g, b);
    }

    private static Bitmap? TryCreateThumbnail(byte[] bytes, int maxEdge)
    {
        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            using var src = Image.FromStream(ms, useEmbeddedColorManagement: false, validateImageData: false);
            var w = src.Width;
            var h = src.Height;
            if (w <= 0 || h <= 0)
                return null;
            var scale = Math.Min(1.0, Math.Min((double)maxEdge / w, (double)maxEdge / h));
            var tw = Math.Max(1, (int)Math.Round(w * scale));
            var th = Math.Max(1, (int)Math.Round(h * scale));
            var bmp = new Bitmap(tw, th);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.DrawImage(src, 0, 0, tw, th);
            }

            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private enum ChatLineKind
    {
        Text,
        Image,
        File,
    }

    private sealed class ChatLine : IDisposable
    {
        private const int ThumbMaxEdge = 240;
        private bool _disposed;
        private Bitmap? _thumbnail;

        public ChatLine(string displayText, Color color, bool outgoing, MessageDeliveryStatus deliveryStatus,
            ChatLineKind kind, byte[]? payloadBytes, string? fileSuggestedName)
        {
            DisplayText = displayText;
            Color = color;
            Outgoing = outgoing;
            DeliveryStatus = deliveryStatus;
            Kind = kind;
            PayloadBytes = payloadBytes;
            FileSuggestedName = fileSuggestedName;
            if (kind == ChatLineKind.Image && payloadBytes is { Length: > 0 })
                _thumbnail = TryCreateThumbnail(payloadBytes, ThumbMaxEdge);
        }

        public string DisplayText { get; }
        public Color Color { get; }
        public bool Outgoing { get; }
        public MessageDeliveryStatus DeliveryStatus { get; }
        public ChatLineKind Kind { get; }
        public byte[]? PayloadBytes { get; }
        public string? FileSuggestedName { get; }
        public Bitmap? Thumbnail => _thumbnail;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _thumbnail?.Dispose();
            _thumbnail = null;
        }

        public override string ToString() => DisplayText;
    }
}
