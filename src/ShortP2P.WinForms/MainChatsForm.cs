using ShortP2P.Client.Data;
using ShortP2P.Client.Routing;
using ShortP2P.Client.Services;

namespace ShortP2P.WinForms;

public sealed class MainChatsForm : Form
{
    private readonly AuthService _auth;
    private readonly ChatRepository _chats;
    private readonly HashSet<int> _knownChatIds = new();
    private readonly ListBox _list = new() { IntegralHeight = false, DrawMode = DrawMode.OwnerDrawFixed };
    private readonly HashSet<int> _newChatIds = new();
    private readonly UserP2pRuntime _p2p;
    private readonly Label _profile = new() { AutoSize = true };
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly P2pRoutingSettingsStore _routingStore;
    private readonly HashSet<int> _unreadChatIds = new();
    private int? _focusedChatId;
    private bool _knownChatsInitialized;

    public MainChatsForm(AuthService auth, ChatRepository chats, UserP2pRuntime p2p,
        P2pRoutingSettingsStore routingStore)
    {
        _auth = auth;
        _chats = chats;
        _p2p = p2p;
        _routingStore = routingStore;
        Text = "ShortP2P — Chats";
        StartPosition = FormStartPosition.CenterScreen;
        Width = 560;
        Height = 480;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var hint = new Label
        {
            Text =
                "P2P chats (UDP). Add a peer manually, or use My QR / QR from file or clipboard in Add chat. Delete chat removes it only on this PC.",
            AutoSize = true,
            ForeColor = SystemColors.GrayText
        };

        var toolbar = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        var btnAdd = new Button { Text = "Add chat", AutoSize = true };
        var btnMyQr = new Button { Text = "My QR", AutoSize = true };
        var btnCopy = new Button { Text = "Copy keys", AutoSize = true };
        var btnLogout = new Button { Text = "Logout", AutoSize = true };
        var btnRouting = new Button { Text = "P2P routing", AutoSize = true };
        var btnLanScan = new Button { Text = "LAN scan", AutoSize = true };
        var btnDelete = new Button { Text = "Delete chat", AutoSize = true };
        toolbar.Controls.Add(btnAdd);
        toolbar.Controls.Add(btnDelete);
        toolbar.Controls.Add(btnMyQr);
        toolbar.Controls.Add(btnCopy);
        toolbar.Controls.Add(btnLanScan);
        toolbar.Controls.Add(btnRouting);
        toolbar.Controls.Add(btnLogout);

        btnAdd.Click += async (_, _) => await OnAddChatAsync().ConfigureAwait(true);
        btnDelete.Click += async (_, _) => await OnDeleteChatAsync().ConfigureAwait(true);
        btnMyQr.Click += OnMyQr;
        btnCopy.Click += OnCopyKeys;
        btnLanScan.Click += OnLanScan;
        btnRouting.Click += OnRoutingSettings;
        btnLogout.Click += OnLogout;

        _list.DisplayMember = nameof(ChatEntity.PeerNickname);
        _list.ValueMember = nameof(ChatEntity.Id);
        _list.DoubleClick += async (_, _) => await OpenSelectedChatAsync().ConfigureAwait(true);
        _list.DrawItem += OnDrawChatItem;

        root.Controls.Add(_profile, 0, 0);
        root.Controls.Add(hint, 0, 1);
        root.Controls.Add(_list, 0, 2);
        root.Controls.Add(toolbar, 0, 3);
        _list.Dock = DockStyle.Fill;
        Controls.Add(root);

        Shown += async (_, _) =>
        {
            var u = _auth.CurrentUser;
            if (u != null)
                try
                {
                    await _p2p.EnsureStartedAsync(u).ConfigureAwait(true);
                    await _p2p.EnsureAllChatSessionsStartedAsync(u, _auth, _chats, SynchronizationContext.Current)
                        .ConfigureAwait(true);
                }
                catch
                {
                    // ignore
                }

            await RefreshAsync().ConfigureAwait(true);
        };
        Activated += async (_, _) => await RefreshAsync().ConfigureAwait(true);
        _chats.ChatListChanged += OnChatListChangedFromInvite;
        _chats.ChatMessageAppended += OnChatMessageAppended;
        FormClosed += (_, _) =>
        {
            _chats.ChatListChanged -= OnChatListChangedFromInvite;
            _chats.ChatMessageAppended -= OnChatMessageAppended;
        };
    }

    private void OnChatMessageAppended(object? sender, ChatMessageAppendedEventArgs e)
    {
        if (e.Outgoing || _focusedChatId == e.ChatId)
            return;
        if (!IsHandleCreated || IsDisposed)
            return;
        try
        {
            BeginInvoke(() =>
            {
                _unreadChatIds.Add(e.ChatId);
                _list.Invalidate();
            });
        }
        catch (ObjectDisposedException)
        {
            // ignore
        }
    }

    private void OnChatListChangedFromInvite(object? sender, EventArgs e)
    {
        if (!IsHandleCreated || IsDisposed)
            return;
        try
        {
            BeginInvoke(() => _ = OnChatListChangedAsync());
        }
        catch (ObjectDisposedException)
        {
            // ignore
        }
    }

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
        catch
        {
            // ignore
        }
    }

    private async Task RefreshAsync()
    {
        await _refreshGate.WaitAsync().ConfigureAwait(true);
        try
        {
            var u = _auth.CurrentUser;
            if (u == null)
            {
                DialogResult = DialogResult.Abort;
                Close();
                return;
            }

            var prevTop = _list.Items.Count > 0 ? _list.TopIndex : 0;
            var prevSelectedId = (_list.SelectedItem as ChatEntity)?.Id;

            _profile.Text = $"You: {u.Nickname} · id {u.NetworkIdShort} · local UDP {u.DataUdpPort}";
            var list = await _chats.ListChatsAsync(u.Id).ConfigureAwait(true);
            var idsNow = list.Select(c => c.Id).ToHashSet();
            if (!_knownChatsInitialized)
            {
                _knownChatIds.Clear();
                foreach (var id in idsNow)
                    _knownChatIds.Add(id);
                _knownChatsInitialized = true;
            }
            else
            {
                foreach (var id in idsNow)
                    if (!_knownChatIds.Contains(id))
                        _newChatIds.Add(id);

                _knownChatIds.Clear();
                foreach (var id in idsNow)
                    _knownChatIds.Add(id);

                _newChatIds.RemoveWhere(id => !idsNow.Contains(id));
            }

            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var c in list)
                _list.Items.Add(c);

            if (prevSelectedId.HasValue)
                foreach (var item in _list.Items)
                    if (item is ChatEntity chat && chat.Id == prevSelectedId.Value)
                    {
                        _list.SelectedItem = item;
                        break;
                    }

            if (_list.Items.Count > 0)
            {
                var safeTop = Math.Clamp(prevTop, 0, _list.Items.Count - 1);
                _list.TopIndex = safeTop;
            }

            _list.EndUpdate();
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private void OnLanScan(object? sender, EventArgs e)
    {
        var u = _auth.CurrentUser;
        if (u == null)
            return;
        using var f = new LocalNetworkScanForm(_p2p, _auth, _chats,
            (chat, owner) =>
            {
                _focusedChatId = chat.Id;
                _unreadChatIds.Remove(chat.Id);
                _list.Invalidate();
                using var win = new ChatForm(chat, u, _auth, _chats, _p2p);
                win.ShowDialog(owner);
                _focusedChatId = null;
            },
            RefreshAsync);
        f.ShowDialog(this);
        _ = RefreshAsync();
    }

    private void OnRoutingSettings(object? sender, EventArgs e)
    {
        using var f = new RoutingSettingsForm(_routingStore, _p2p);
        f.ShowDialog(this);
    }

    private void OnMyQr(object? sender, EventArgs e)
    {
        using var f = new MyQrForm(_auth);
        f.ShowDialog(this);
    }

    private async Task OnAddChatAsync()
    {
        using var dlg = new AddChatForm();
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        var u = _auth.CurrentUser;
        if (u == null) return;

        try
        {
            RsaKeySerializer.DeserializePublic(dlg.PeerPublicKeyJson);
        }
        catch
        {
            MessageBox.Show(this, "Invalid public key JSON.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        await _chats.AddChatAsync(u.Id, dlg.PeerNickname, dlg.PeerNetworkIdShort, dlg.PeerPublicKeyJson.Trim(),
            dlg.PeerHost, dlg.PeerPort).ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
    }

    private async Task OnDeleteChatAsync()
    {
        if (_list.SelectedItem is not ChatEntity chat)
        {
            MessageBox.Show(this, "Выберите чат в списке.", "Удаление", MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var u = _auth.CurrentUser;
        if (u == null)
            return;

        var ok = MessageBox.Show(this,
            $"Удалить чат «{chat.PeerNickname}» только на этом устройстве?\nИстория сообщений будет удалена.",
            "Удаление чата",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);
        if (ok != DialogResult.Yes)
            return;

        await _p2p.RemoveChatSessionAsync(chat.Id).ConfigureAwait(true);
        var removed = await _chats.DeleteChatAsync(chat.Id, u.Id).ConfigureAwait(true);
        if (!removed)
        {
            MessageBox.Show(this, "Не удалось удалить чат.", "Удаление", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _newChatIds.Remove(chat.Id);
        _unreadChatIds.Remove(chat.Id);
        _knownChatIds.Remove(chat.Id);
        await RefreshAsync().ConfigureAwait(true);
    }

    private void OnCopyKeys(object? sender, EventArgs e)
    {
        var u = _auth.CurrentUser;
        if (u == null) return;
        var pub = RsaKeySerializer.SerializePublic(_auth.GetCurrentPublicKey());
        var text = $"Network id: {u.NetworkIdShort}\r\nPublic key JSON:\r\n{pub}";
        try
        {
            Clipboard.SetText(text);
            MessageBox.Show(this, "Network id and public key JSON copied to clipboard.", "Copied",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Clipboard", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async void OnLogout(object? sender, EventArgs e)
    {
        try
        {
            await _p2p.StopAsync().ConfigureAwait(true);
        }
        catch
        {
            // ignore
        }

        await _auth.LogoutAsync().ConfigureAwait(true);
        DialogResult = DialogResult.Retry;
        Close();
    }

    private async Task OpenSelectedChatAsync()
    {
        if (_list.SelectedItem is not ChatEntity chat)
            return;
        _newChatIds.Remove(chat.Id);
        _unreadChatIds.Remove(chat.Id);
        _list.Invalidate();

        var u = _auth.CurrentUser;
        if (u == null) return;

        _focusedChatId = chat.Id;
        using var win = new ChatForm(chat, u, _auth, _chats, _p2p);
        win.ShowDialog(this);
        _focusedChatId = null;
        await RefreshAsync().ConfigureAwait(true);
    }

    private void OnDrawChatItem(object? sender, DrawItemEventArgs e)
    {
        e.DrawBackground();
        if (e.Index < 0 || e.Index >= _list.Items.Count)
            return;

        if (_list.Items[e.Index] is not ChatEntity chat)
            return;

        var emphasize = _unreadChatIds.Contains(chat.Id) || _newChatIds.Contains(chat.Id);
        var baseFont = e.Font ?? _list.Font;
        using var drawFont = emphasize ? new Font(baseFont, FontStyle.Bold) : null;
        var font = drawFont ?? baseFont;
        TextRenderer.DrawText(e.Graphics, chat.PeerNickname, font, e.Bounds, e.ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        e.DrawFocusRectangle();
    }
}