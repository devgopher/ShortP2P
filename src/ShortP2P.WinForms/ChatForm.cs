using System.Globalization;
using Microsoft.Extensions.Logging;
using ShortP2P.Client;
using ShortP2P.Client.Data;
using ShortP2P.Client.Services;

namespace ShortP2P.WinForms;

public sealed class ChatForm : Form
{
    private readonly ChatEntity _chat;
    private readonly UserEntity _user;
    private readonly AuthService _auth;
    private readonly ChatRepository _repo;
    private readonly UserP2pRuntime _p2PRuntime;
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
    private readonly Button _send = new() { Text = "Send", Dock = DockStyle.Right, AutoSize = true };
    private ChatP2pSession? _p2PSession;

    public ChatForm(ChatEntity chat, UserEntity user, AuthService auth, ChatRepository repo, UserP2pRuntime p2PRuntime,
        ILogger<ChatForm> logger, ILogger<UserAction> userActions)
    {
        _chat = chat;
        _user = user;
        _auth = auth;
        _repo = repo;
        _p2PRuntime = p2PRuntime;
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

        var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 88, ColumnCount = 2 };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.Controls.Add(_input, 0, 0);
        bottom.Controls.Add(_send, 1, 0);

        // Порядок: Fill сначала, затем Top/Bottom — иначе между шапкой и вводом остаётся пустая полоса.
        Controls.Add(_messages);
        Controls.Add(top);
        Controls.Add(bottom);

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
            _messages.Items.Clear();
            foreach (var m in rows)
            {
                var sender = m.Outgoing ? "You" : _chat.PeerNickname;
                var color = m.Outgoing ? Color.DodgerBlue : GetPaletteColor(sender);
                var sentLocal = new DateTimeOffset(m.SentUtcTicks, TimeSpan.Zero).ToLocalTime();
                var full = $"[{sentLocal.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture)}] {m.Text}";
                var ds = (MessageDeliveryStatus)m.DeliveryStatus;
                if (m.Outgoing && ds == MessageDeliveryStatus.NotApplicable)
                    ds = MessageDeliveryStatus.Delivered;
                _messages.Items.Add(new ChatLine(full, color, m.Outgoing, ds));
            }
            _messages.EndUpdate();
        }
        catch (ObjectDisposedException)
        {
            // expected while closing
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

    private void OnMessageDoubleClick(object? sender, EventArgs e)
    {
        if (_messages.SelectedItem is not ChatLine line)
            return;
        if (line.Text.Length <= 64)
            return;
        _userActions.LogInformation("Chat {Peer}: open long message viewer", _chat.PeerNickname);
        using var dlg = new MessageViewForm("Сообщение", line.Text);
        dlg.ShowDialog(this);
    }

    private void OnMessagesDrawItem(object? sender, DrawItemEventArgs e)
    {
        e.DrawBackground();
        if (e.Index < 0 || e.Index >= _messages.Items.Count)
            return;

        var line = _messages.Items[e.Index] as ChatLine;
        var text = line?.Text ?? _messages.Items[e.Index]?.ToString() ?? "";
        var color = line?.Color ?? ForeColor;
        const int statusCol = 22;
        var reserveRight = line is { Outgoing: true } ? statusCol : 0;
        var textBounds = new Rectangle(e.Bounds.X + 4, e.Bounds.Y + 2,
            Math.Max(10, e.Bounds.Width - 8 - reserveRight), Math.Max(10, e.Bounds.Height - 4));
        var font = e.Font ?? _messages.Font;
        TextRenderer.DrawText(e.Graphics, text, font, textBounds, color,
            TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);

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
        var text = line?.Text ?? _messages.Items[e.Index]?.ToString() ?? "";
        const int statusCol = 22;
        var reserveRight = line is { Outgoing: true } ? statusCol : 0;
        var width = Math.Max(120,
            _messages.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 12 - reserveRight);
        var measured = TextRenderer.MeasureText(text, _messages.Font, new Size(width, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        e.ItemHeight = Math.Max(_messages.Font.Height + 8, measured.Height + 8);
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
        var idx = hash % 64;
        var hue = idx * (360.0 / 64.0);
        return ColorFromHsl(hue, 0.72, 0.44);
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

    private sealed record ChatLine(string Text, Color Color, bool Outgoing, MessageDeliveryStatus DeliveryStatus)
    {
        public override string ToString() => Text;
    }
}
