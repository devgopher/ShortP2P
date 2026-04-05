using ShortP2P.Client.Data;
using ShortP2P.Client.Services;

namespace ShortP2P.WinForms;

public sealed class ChatForm : Form
{
    private readonly ChatEntity _chat;
    private readonly UserEntity _user;
    private readonly AuthService _auth;
    private readonly ChatRepository _repo;
    private readonly UserP2pRuntime _p2pRuntime;
    private readonly ListBox _messages = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly TextBox _input = new() { Dock = DockStyle.Fill };
    private readonly Button _send = new() { Text = "Send", Dock = DockStyle.Right, AutoSize = true };
    private ChatP2pSession? _p2pSession;

    public ChatForm(ChatEntity chat, UserEntity user, AuthService auth, ChatRepository repo, UserP2pRuntime p2pRuntime)
    {
        _chat = chat;
        _user = user;
        _auth = auth;
        _repo = repo;
        _p2pRuntime = p2pRuntime;
        Text = chat.PeerNickname;
        StartPosition = FormStartPosition.CenterParent;
        Width = 520;
        Height = 480;

        var peerMeta = new Label
        {
            Dock = DockStyle.Top,
            Height = 44,
            AutoSize = false,
            ForeColor = SystemColors.GrayText,
            Padding = new Padding(8, 6, 8, 4),
            Text = $"Id: {chat.PeerNetworkIdShort}\r\nАдрес: {chat.PeerHost}:{chat.PeerPort}",
        };

        var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 36, ColumnCount = 2 };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.Controls.Add(_input, 0, 0);
        bottom.Controls.Add(_send, 1, 0);

        Controls.Add(peerMeta);
        Controls.Add(_messages);
        Controls.Add(bottom);

        _send.Click += async (_, _) => await OnSendAsync().ConfigureAwait(true);
        Shown += async (_, _) => await OnShownAsync().ConfigureAwait(true);
    }

    private async Task OnShownAsync()
    {
        var uiSync = SynchronizationContext.Current;
        _p2pSession = new ChatP2pSession(_chat, _user, _auth, _repo, uiSync, _p2pRuntime.Gateway, _p2pRuntime.Settings);
        _p2pSession.MessagesChanged += OnP2pMessagesChanged;
        try
        {
            await _p2pSession.StartAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not start UDP: {ex.Message}", "P2P", MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        await ReloadMessagesAsync().ConfigureAwait(true);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (_p2pSession != null)
        {
            _p2pSession.MessagesChanged -= OnP2pMessagesChanged;
            _p2pSession.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _p2pSession = null;
        }

        base.OnFormClosed(e);
    }

    private void OnP2pMessagesChanged(object? sender, EventArgs e) =>
        BeginInvoke(new Action(() => _ = ReloadMessagesAsync()));

    private async Task ReloadMessagesAsync()
    {
        try
        {
            var rows = await _repo.ListMessagesAsync(_chat.Id).ConfigureAwait(true);
            if (!IsHandleCreated || IsDisposed)
                return;
            _messages.BeginUpdate();
            _messages.Items.Clear();
            foreach (var m in rows)
                _messages.Items.Add($"{(m.Outgoing ? "You" : "Peer")}: {m.Text}");
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
        if (text.Length == 0 || _p2pSession == null)
            return;

        try
        {
            await _p2pSession.SendTextAsync(text).ConfigureAwait(true);
            _input.Clear();
            await ReloadMessagesAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Send failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
