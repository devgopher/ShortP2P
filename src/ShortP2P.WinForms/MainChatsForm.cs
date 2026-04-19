using System.Drawing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShortP2P.Client.ChatMedia;
using ShortP2P.Client.Data;
using ShortP2P.Client.Routing;
using ShortP2P.Client.Services;

namespace ShortP2P.WinForms;

public sealed class MainChatsForm : Form
{
    private readonly AuthService _auth;
    private readonly ChatRepository _chats;
    private readonly HashSet<int> _knownChatIds = [];
    private readonly ListBox _list = new() { IntegralHeight = false, DrawMode = DrawMode.OwnerDrawFixed };
    private readonly HashSet<int> _newChatIds = [];
    private readonly UserP2pRuntime _p2P;
    private readonly Label _profile = new() { AutoSize = true };
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly P2pRoutingSettingsStore _routingStore;
    private readonly HashSet<int> _unreadChatIds = [];
    private readonly IServiceProvider _services;
    private readonly ILogger<MainChatsForm> _logger;
    private readonly ILogger<ChatForm> _chatLog;
    private readonly ILogger<LocalNetworkScanForm> _lanScanLog;
    private readonly ILogger<UserAction> _userActions;
    private readonly ChatMediaOptions _chatMedia;
    private int? _focusedChatId;
    private bool _knownChatsInitialized;

    public MainChatsForm(AuthService auth, ChatRepository chats, UserP2pRuntime p2P,
        P2pRoutingSettingsStore routingStore, IServiceProvider services, ILogger<MainChatsForm> logger,
        ILogger<ChatForm> chatLog, ILogger<LocalNetworkScanForm> lanScanLog, ILogger<UserAction> userActions,
        ChatMediaOptions chatMedia)
    {
        _auth = auth;
        _chats = chats;
        _p2P = p2P;
        _routingStore = routingStore;
        _services = services;
        _logger = logger;
        _chatLog = chatLog;
        _lanScanLog = lanScanLog;
        _userActions = userActions;
        _chatMedia = chatMedia;
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
            {
                _userActions.LogInformation(
                    "Chats: main window opened (user {Nickname}, network id {NetworkId})",
                    u.Nickname, u.NetworkIdShort);
                try
                {
                    await _p2P.EnsureStartedAsync(u).ConfigureAwait(true);
                    await _p2P.EnsureAllChatSessionsStartedAsync(u, _auth, _chats, SynchronizationContext.Current)
                        .ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Ensure P2P sessions on main window shown");
                }
            }

            await RefreshAsync().ConfigureAwait(true);
        };
        Activated += async (_, _) => await RefreshAsync().ConfigureAwait(true);
        _chats.ChatListChanged += OnChatListChangedFromInvite;
        _chats.ChatMessageAppended += OnChatMessageAppended;
        _p2P.LocalScan.ClientsChanged += OnLanPresenceChanged;
        FormClosed += (_, _) =>
        {
            _chats.ChatListChanged -= OnChatListChangedFromInvite;
            _chats.ChatMessageAppended -= OnChatMessageAppended;
            _p2P.LocalScan.ClientsChanged -= OnLanPresenceChanged;
        };
    }

    private void OnLanPresenceChanged(object? sender, EventArgs e)
    {
        if (!IsHandleCreated || IsDisposed)
            return;
        try
        {
            BeginInvoke(() => _list.Invalidate());
        }
        catch (ObjectDisposedException)
        {
            // ignore
        }
    }

    private void OnChatMessageAppended(object? sender, ChatMessageAppendedEventArgs e)
    {
        if (!e.Outgoing)
            IncomingMessageSound.TryPlay();

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
            await _p2P.EnsureAllChatSessionsStartedAsync(u, _auth, _chats, SynchronizationContext.Current)
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ensure all chat sessions after list change");
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
                _userActions.LogInformation("Chats: session lost, closing main window");
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
        _userActions.LogInformation("Chats: open LAN scan");
        using var f = new LocalNetworkScanForm(_p2P, _auth, _chats, _lanScanLog, _userActions,
            (chat, owner) =>
            {
                _userActions.LogInformation("Chats: open chat from LAN scan (peer {Peer}, id {ChatId})",
                    chat.PeerNickname, chat.Id);
                _focusedChatId = chat.Id;
                _unreadChatIds.Remove(chat.Id);
                _list.Invalidate();
                using var win = new ChatForm(chat, u, _auth, _chats, _p2P, _chatLog, _userActions, _chatMedia);
                win.ShowDialog(owner);
                _focusedChatId = null;
            },
            RefreshAsync);
        f.ShowDialog(this);
        _ = RefreshAsync();
    }

    private void OnRoutingSettings(object? sender, EventArgs e)
    {
        _userActions.LogInformation("Chats: open P2P routing settings");
        using var f = _services.GetRequiredService<RoutingSettingsForm>();
        f.ShowDialog(this);
    }

    private void OnMyQr(object? sender, EventArgs e)
    {
        _userActions.LogInformation("Chats: open My QR");
        using var f = _services.GetRequiredService<MyQrForm>();
        f.ShowDialog(this);
    }

    private async Task OnAddChatAsync()
    {
        _userActions.LogInformation("Chats: open add chat");
        using var dlg = _services.GetRequiredService<AddChatForm>();
        if (dlg.ShowDialog(this) != DialogResult.OK)
        {
            _userActions.LogInformation("Chats: add chat cancelled");
            return;
        }

        var u = _auth.CurrentUser;
        if (u == null) return;

        try
        {
            RsaKeySerializer.DeserializePublic(dlg.PeerPublicKeyJson);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invalid public key JSON when adding chat");
            MessageBox.Show(this, "Invalid public key JSON.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        await _chats.AddChatAsync(u.Id, dlg.PeerNickname, dlg.PeerNetworkIdShort, dlg.PeerPublicKeyJson.Trim(),
            dlg.PeerHost, dlg.PeerPort).ConfigureAwait(true);
        _userActions.LogInformation(
            "Chats: chat added (peer {Peer}, network id {NetworkId}, host {Host}:{Port})",
            dlg.PeerNickname, dlg.PeerNetworkIdShort, dlg.PeerHost, dlg.PeerPort);
        await RefreshAsync().ConfigureAwait(true);
    }

    private async Task OnDeleteChatAsync()
    {
        _userActions.LogInformation("Chats: delete chat requested");
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

        await _p2P.RemoveChatSessionAsync(chat.Id).ConfigureAwait(true);
        var removed = await _chats.DeleteChatAsync(chat.Id, u.Id).ConfigureAwait(true);
        if (!removed)
        {
            _userActions.LogInformation("Chats: delete chat failed (peer {Peer}, id {ChatId})",
                chat.PeerNickname, chat.Id);
            MessageBox.Show(this, "Не удалось удалить чат.", "Удаление", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _userActions.LogInformation("Chats: chat deleted (peer {Peer}, id {ChatId})", chat.PeerNickname, chat.Id);
        _newChatIds.Remove(chat.Id);
        _unreadChatIds.Remove(chat.Id);
        _knownChatIds.Remove(chat.Id);
        await RefreshAsync().ConfigureAwait(true);
    }

    private void OnCopyKeys(object? sender, EventArgs e)
    {
        var u = _auth.CurrentUser;
        if (u == null) return;
        _userActions.LogInformation("Chats: copy keys to clipboard");
        var pub = RsaKeySerializer.SerializePublic(_auth.GetCurrentPublicKey());
        var text = $"Network id: {u.NetworkIdShort}\r\nPublic key JSON:\r\n{pub}";
        try
        {
            Clipboard.SetText(text);
            MessageBox.Show(this, "Network id and public key JSON copied to clipboard.", "Copied",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            _userActions.LogInformation("Chats: keys copied successfully");
        }
        catch (Exception ex)
        {
            _userActions.LogInformation("Chats: copy keys failed ({Message})", ex.Message);
            MessageBox.Show(this, ex.Message, "Clipboard", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async void OnLogout(object? sender, EventArgs e)
    {
        _userActions.LogInformation("Chats: logout");
        try
        {
            await _p2P.StopAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Stop P2P on logout");
        }

        await _auth.LogoutAsync().ConfigureAwait(true);
        DialogResult = DialogResult.Retry;
        Close();
    }

    private async Task OpenSelectedChatAsync()
    {
        if (_list.SelectedItem is not ChatEntity chat)
            return;
        _userActions.LogInformation("Chats: open chat (peer {Peer}, id {ChatId})", chat.PeerNickname, chat.Id);
        _newChatIds.Remove(chat.Id);
        _unreadChatIds.Remove(chat.Id);
        _list.Invalidate();

        var u = _auth.CurrentUser;
        if (u == null) return;

        _focusedChatId = chat.Id;
        using var win = new ChatForm(chat, u, _auth, _chats, _p2P, _chatLog, _userActions, _chatMedia);
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

        const int dotSize = 10;
        const int dotPad = 4;
        var online = _p2P.LocalScan.IsPeerSeenRecentlyOnLan(chat.PeerNetworkIdShort);
        var dotBrush = online ? Brushes.ForestGreen : Brushes.IndianRed;
        var cy = e.Bounds.Top + (e.Bounds.Height - dotSize) / 2;
        e.Graphics.FillEllipse(dotBrush, e.Bounds.Left + dotPad, cy, dotSize, dotSize);

        var emphasize = _unreadChatIds.Contains(chat.Id) || _newChatIds.Contains(chat.Id);
        var baseFont = e.Font ?? _list.Font;
        using var drawFont = emphasize ? new Font(baseFont, FontStyle.Bold) : null;
        var font = drawFont ?? baseFont;
        var textBounds = new Rectangle(e.Bounds.Left + dotPad * 2 + dotSize, e.Bounds.Top,
            e.Bounds.Width - dotPad * 2 - dotSize, e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, chat.PeerNickname, font, textBounds, e.ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        e.DrawFocusRectangle();
    }
}
