using System.Net;
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
    private readonly Label _peerIdLabel = new()
    {
        AutoSize = true,
        ForeColor = SystemColors.GrayText,
        Padding = new Padding(0, 0, 0, 4),
    };
    private readonly Label _peerStatusLabel = new()
    {
        AutoSize = true,
        ForeColor = SystemColors.GrayText,
        Padding = new Padding(0, 0, 0, 4),
    };
    
    private readonly TextBox _peerHostEntry = new() { Width = 200 };
    private readonly TextBox _peerPortEntry = new() { Width = 56 };
    private readonly Button _applyPeerEndpoint = new() { Text = "Применить", AutoSize = true };
    private readonly ListBox _messages = new()
    {
        Dock = DockStyle.Bottom,
        IntegralHeight = false,
        Height = 300,
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

    public ChatForm(ChatEntity chat, UserEntity user, AuthService auth, ChatRepository repo, UserP2pRuntime p2PRuntime)
    {
        _chat = chat;
        _user = user;
        _auth = auth;
        _repo = repo;
        _p2PRuntime = p2PRuntime;
        Text = chat.PeerNickname;
        StartPosition = FormStartPosition.CenterParent;
        Width = 520;
        Height = 520;
        MaximizeBox = false;

        _peerIdLabel.Text = $"Id: {chat.PeerNetworkIdShort}";
        _peerStatusLabel.Text = "Статус: офлайн";
        _peerHostEntry.Text = chat.PeerHost;
        _peerPortEntry.Text = chat.PeerPort.ToString();

        var top = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(8, 6, 8, 4),
            ColumnCount = 1,
            RowCount = 3
        };
        top.Controls.Add(_peerIdLabel, 0, 0);
        top.Controls.Add(_peerStatusLabel, 0, 1);

        var addrRow = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
        };
        addrRow.Controls.Add(new Label { Text = "IP:", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
        addrRow.Controls.Add(_peerHostEntry);
        addrRow.Controls.Add(new Label { Text = "Порт:", AutoSize = true, Padding = new Padding(8, 6, 4, 0) });
        addrRow.Controls.Add(_peerPortEntry);
        addrRow.Controls.Add(_applyPeerEndpoint);
        top.Controls.Add(addrRow, 0, 2);

        var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 88, ColumnCount = 2 };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.Controls.Add(_input, 0, 0);
        bottom.Controls.Add(_send, 1, 0);

        Controls.Add(top);
        Controls.Add(_messages);
        Controls.Add(bottom);

        _applyPeerEndpoint.Click += async (_, _) => await OnApplyPeerEndpointAsync().ConfigureAwait(true);
        _send.Click += async (_, _) => await OnSendAsync().ConfigureAwait(true);
        _messages.DrawItem += OnMessagesDrawItem;
        _messages.MeasureItem += OnMessagesMeasureItem;
        _messages.DoubleClick += OnMessageDoubleClick;
        Shown += async (_, _) => await OnShownAsync().ConfigureAwait(true);
        FormClosed += (_, _) => _p2PRuntime.PeerPresenceChanged -= OnPeerPresenceChanged;
    }

    private async Task OnShownAsync()
    {
        var uiSync = SynchronizationContext.Current;
        var fresh = await _repo.GetChatAsync(_chat.Id).ConfigureAwait(true) ?? _chat;
        _p2PSession = _p2PRuntime.GetOrCreateSession(fresh, _user, _auth, _repo, uiSync);
        _p2PSession.MessagesChanged += OnP2pMessagesChanged;
        _p2PRuntime.PeerPresenceChanged += OnPeerPresenceChanged;
        if (!_p2PRuntime.IsChatSessionStarted(_chat.Id))
        {
            try
            {
                await _p2PSession.StartAsync().ConfigureAwait(true);
                _p2PRuntime.MarkChatSessionStarted(_chat.Id);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Could not start UDP: {ex.Message}", "P2P", MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        await ReloadMessagesAsync().ConfigureAwait(true);
        RefreshPeerPresenceLabel();
    }

    private async Task OnApplyPeerEndpointAsync()
    {
        if (_p2PSession == null)
        {
            MessageBox.Show(this, "Сессия ещё не готова.", "Чат", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var host = _peerHostEntry.Text.Trim();
        if (host.Length == 0)
        {
            MessageBox.Show(this, "Укажите IP.", "Адрес", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!int.TryParse(_peerPortEntry.Text.Trim(), out var port) || port is < 1 or > 65535)
        {
            MessageBox.Show(this, "Порт должен быть 1–65535.", "Адрес", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            _ = IPAddress.Parse(host);
        }
        catch
        {
            MessageBox.Show(this, "Некорректный IP.", "Адрес", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            await _p2PSession.ApplyPeerEndpointAsync(host, port).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Адрес", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _peerHostEntry.Text = _chat.PeerHost;
        _peerPortEntry.Text = _chat.PeerPort.ToString();
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
                var full = $"{sender}: {m.Text}";
                _messages.Items.Add(new ChatLine(full, color));
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
            _input.Clear();
            await ReloadMessagesAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Send failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OnPeerPresenceChanged(object? sender, ShortP2P.Client.Routing.PeerPresenceChangedEventArgs e)
    {
        var current = ShortP2P.Discovery.CompressedNetworkId.FromShortString(_chat.PeerNetworkIdShort).Value;
        if (e.PeerNetworkId != current)
            return;
        if (!IsHandleCreated || IsDisposed)
            return;
        BeginInvoke(RefreshPeerPresenceLabel);
    }

    private void RefreshPeerPresenceLabel()
    {
        var online = _p2PRuntime.IsPeerOnline(_chat.PeerNetworkIdShort);
        _peerStatusLabel.Text = online ? "Статус: онлайн" : "Статус: офлайн";
        _peerStatusLabel.ForeColor = online ? Color.SeaGreen : SystemColors.GrayText;
    }

    private void OnMessageDoubleClick(object? sender, EventArgs e)
    {
        if (_messages.SelectedItem is not ChatLine line)
            return;
        if (line.Text.Length <= 64)
            return;

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
        var textBounds = new Rectangle(e.Bounds.X + 4, e.Bounds.Y + 2, Math.Max(10, e.Bounds.Width - 8),
            Math.Max(10, e.Bounds.Height - 4));
        TextRenderer.DrawText(e.Graphics, text, e.Font, textBounds, color,
            TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
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
        var width = Math.Max(120, _messages.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 12);
        var measured = TextRenderer.MeasureText(text, _messages.Font, new Size(width, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        e.ItemHeight = Math.Max(_messages.Font.Height + 8, measured.Height + 8);
    }

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

    private sealed record ChatLine(string Text, Color Color)
    {
        public override string ToString() => Text;
    }
}
