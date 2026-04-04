using ShortP2P.Client.Data;
using ShortP2P.Client.Services;

namespace ShortP2P.WinForms;

public sealed class ChatForm : Form
{
    private readonly ChatEntity _chat;
    private readonly UserEntity _user;
    private readonly AuthService _auth;
    private readonly ChatRepository _repo;
    private readonly ListBox _messages = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly TextBox _input = new() { Dock = DockStyle.Fill };
    private readonly Button _send = new() { Text = "Send", Dock = DockStyle.Right, AutoSize = true };
    private ChatP2pSession? _p2p;

    public ChatForm(ChatEntity chat, UserEntity user, AuthService auth, ChatRepository repo)
    {
        _chat = chat;
        _user = user;
        _auth = auth;
        _repo = repo;
        Text = chat.PeerNickname;
        StartPosition = FormStartPosition.CenterParent;
        Width = 520;
        Height = 480;

        var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 36, ColumnCount = 2 };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.Controls.Add(_input, 0, 0);
        bottom.Controls.Add(_send, 1, 0);

        Controls.Add(_messages);
        Controls.Add(bottom);

        _send.Click += async (_, _) => await OnSendAsync().ConfigureAwait(true);
        Shown += async (_, _) => await OnShownAsync().ConfigureAwait(true);
    }

    private async Task OnShownAsync()
    {
        var uiSync = SynchronizationContext.Current;
        _p2p = new ChatP2pSession(_chat, _user, _auth, _repo, uiSync);
        _p2p.MessagesChanged += OnP2pMessagesChanged;
        try
        {
            await _p2p.StartAsync().ConfigureAwait(true);
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
        if (_p2p != null)
        {
            _p2p.MessagesChanged -= OnP2pMessagesChanged;
            _p2p.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _p2p = null;
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
        if (text.Length == 0 || _p2p == null)
            return;

        try
        {
            await _p2p.SendTextAsync(text).ConfigureAwait(true);
            _input.Clear();
            await ReloadMessagesAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Send failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
