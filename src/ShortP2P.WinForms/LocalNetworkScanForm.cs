using Microsoft.Extensions.Logging;
using ShortP2P.Auth;
using ShortP2P.Client.Data;
using ShortP2P.Client.Routing;
using ShortP2P.Client.Services;
using ShortP2P.Discovery;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.WinForms;

/// <summary>Ручное сканирование LAN: presence UDP 17501; discovery wire — UdpPeerDiscoveryOptions (17890).</summary>
public sealed class LocalNetworkScanForm : Form
{
    private readonly AuthService _auth;
    private readonly ChatRepository _chats;
    private readonly Button _close = new() { Text = "Закрыть", DialogResult = DialogResult.OK };

    private readonly ListView _list = new()
    {
        View = View.Details,
        FullRowSelect = true,
        GridLines = true,
        Dock = DockStyle.Fill,
        MultiSelect = false
    };

    private readonly ILogger<LocalNetworkScanForm> _logger;
    private readonly Action<ChatEntity, IWin32Window> _openChat;
    private readonly UserP2pRuntime _p2P;
    private readonly Func<Task>? _refreshMainChatsAsync;
    private readonly Button _scan = new() { Text = "Сканировать", AutoSize = true };

    private readonly Label _status = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Text = "" };
    private readonly ILogger<UserAction> _userActions;

    public LocalNetworkScanForm(UserP2pRuntime p2P, AuthService auth, ChatRepository chats,
        ILogger<LocalNetworkScanForm> logger, ILogger<UserAction> userActions,
        Action<ChatEntity, IWin32Window> openChat, Func<Task>? refreshMainChatsAsync = null)
    {
        _p2P = p2P;
        _auth = auth;
        _chats = chats;
        _logger = logger;
        _userActions = userActions;
        _openChat = openChat;
        _refreshMainChatsAsync = refreshMainChatsAsync;
        Text = "Локальная сеть";
        StartPosition = FormStartPosition.CenterParent;
        Width = 720;
        Height = 420;
        MinimizeBox = false;

        _list.Columns.Add("Ник", 140);
        _list.Columns.Add("Network id", 180);
        _list.Columns.Add("Транспорт", 80);
        _list.Columns.Add("Статус", 80);
        _list.Columns.Add("Последний контакт", 140);

        var bottom = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Dock = DockStyle.Bottom,
            Padding = new Padding(0, 8, 0, 0)
        };
        bottom.Controls.Add(_scan);
        bottom.Controls.Add(_close);

        var scanHint = new Label
        {
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Text =
                "Сканирование LAN (UDP/Bluetooth) и опрос messenger-серверов (GetClients). " +
                "Двойной щелчок по строке (или Enter) — открыть чат или создать новый; список на главном экране обновится сам."
        };

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
        root.Controls.Add(scanHint, 0, 0);
        root.Controls.Add(_status, 0, 1);
        root.Controls.Add(_list, 0, 2);
        root.Controls.Add(bottom, 0, 3);
        Controls.Add(root);

        _scan.Click += async (_, _) => await OnScanAsync().ConfigureAwait(true);
        _list.ItemActivate += async (_, _) => await OnListItemActivateAsync().ConfigureAwait(true);
        AcceptButton = _close;

        Shown += (_, _) =>
        {
            _p2P.LocalScan.ClientsChanged += OnClientsChanged;
            RefreshList();
        };
        FormClosed += (_, _) => _p2P.LocalScan.ClientsChanged -= OnClientsChanged;
    }

    private void OnClientsChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(RefreshList);
        }
        catch (ObjectDisposedException)
        {
            // ignore
        }
    }

    private void RefreshList()
    {
        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            foreach (var p in _p2P.LocalScan.Clients)
            {
                var idShort = p.NetworkId.ToShortString();
                var row = new ListViewItem(string.IsNullOrEmpty(p.Nickname) ? "—" : p.Nickname)
                {
                    Tag = p
                };
                row.SubItems.Add(idShort);
                row.SubItems.Add(FormatTransport(p.TransportKind));
                var online = p.TransportKind == TransportKind.MessengerServer
                    ? p.MessengerServerOnline
                    : _p2P.LocalScan.IsPeerSeenRecentlyOnLan(idShort) || p.MessengerServerOnline;
                row.SubItems.Add(online ? "онлайн" : "офлайн");
                row.SubItems.Add(p.LastSeenUtc.ToLocalTime().ToString("T"));
                _list.Items.Add(row);
            }
        }
        finally
        {
            _list.EndUpdate();
        }
    }

    private static string FormatTransport(TransportKind k)
    {
        return k switch
        {
            TransportKind.Udp => "UDP",
            TransportKind.Bluetooth => "Bluetooth",
            TransportKind.Infrared => "IrDA",
            TransportKind.MessengerServer => "Сервер",
            _ => k.ToString()
        };
    }

    private async Task OnListItemActivateAsync()
    {
        if (_list.SelectedItems.Count == 0)
            return;
        if (_list.SelectedItems[0].Tag is not DiscoveredLocalPeer peer)
            return;

        try
        {
            var result = await LanChatStartFromDiscovery
                .TryStartAsync(peer, _auth, _chats, _p2P, CancellationToken.None).ConfigureAwait(true);

            switch (result.Kind)
            {
                case LanChatStartKind.AlreadyExists:
                case LanChatStartKind.Created:
                    _userActions.LogInformation(
                        "LAN scan: start chat from peer {Nickname} (network id {NetworkId}, kind {Kind})",
                        peer.Nickname, peer.NetworkId.ToShortString(), result.Kind);
                    if (_refreshMainChatsAsync != null)
                        await _refreshMainChatsAsync().ConfigureAwait(true);
                    if (result.Chat != null)
                        _openChat(result.Chat, this);
                    break;
                case LanChatStartKind.WaitingForPeer:
                    _userActions.LogInformation(
                        "LAN scan: waiting for peer {Nickname} (network id {NetworkId})",
                        peer.Nickname, peer.NetworkId.ToShortString());
                    MessageBox.Show(this, result.Message ?? "", "LAN", MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    break;
                case LanChatStartKind.Failed:
                    _userActions.LogInformation(
                        "LAN scan: start chat failed for {Nickname} ({Message})",
                        peer.Nickname, result.Message ?? "unknown");
                    MessageBox.Show(this, result.Message ?? "Ошибка", "LAN", MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LAN scan item activate");
            MessageBox.Show(this, ex.Message, "LAN", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task OnScanAsync()
    {
        _userActions.LogInformation("LAN scan: scan started");
        _scan.Enabled = false;
        var sec = (int)Math.Round(LocalNetworkScanner.DefaultScanListenDuration.TotalSeconds);
        _status.Text = $"Сканируем LAN и серверы {sec} с…";
        try
        {
            await _p2P.LocalScan.ScanAsync(LocalNetworkScanner.DefaultScanListenDuration).ConfigureAwait(true);
            RefreshList();
            _userActions.LogInformation("LAN scan: scan finished ({Count} peers in list)", _list.Items.Count);
        }
        finally
        {
            _status.Text = "";
            _scan.Enabled = true;
        }
    }
}