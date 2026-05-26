using System.Drawing;
using System.Drawing.Imaging;
using Microsoft.Extensions.Logging;
using ShortP2P.Auth.Data;
using ShortP2P.Transport;
using ShortP2P.Client.Qr;

namespace ShortP2P.WinForms;

public sealed class AddChatForm : Form
{
    private readonly ILogger<AddChatForm> _logger;
    private readonly ILogger<UserAction> _userActions;
    private readonly TextBox _nick = new() { PlaceholderText = "Peer nickname" };
    private readonly TextBox _id = new() { PlaceholderText = "Peer network id" };
    private readonly TextBox _pub = new()
    {
        PlaceholderText = "Peer RSA public key JSON", Multiline = true, Height = 80, ScrollBars = ScrollBars.Vertical
    };
    private readonly TextBox _host = new() { PlaceholderText = "Peer IP / network id / Bluetooth MAC" };
    private readonly TextBox _port = new() { PlaceholderText = "UDP port" };
    private readonly Button _btnOk = new() { Text = "Save" };
    private readonly Button _btnCancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel };

    public string PeerNickname => _nick.Text.Trim();
    public string PeerNetworkIdShort => _id.Text.Trim();
    public string PeerPublicKeyJson => _pub.Text.Trim();
    public string PeerHosts => _host.Text.Trim();
    public int PeerPort => int.TryParse(_port.Text, out var p) ? p : 0;

    public AddChatForm(ILogger<AddChatForm> logger, ILogger<UserAction> userActions)
    {
        _logger = logger;
        _userActions = userActions;
        Text = "Add chat";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Width = 420;
        Height = 460;
        Padding = new Padding(12);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(_nick, 0, 0);
        layout.Controls.Add(_id, 0, 1);
        layout.Controls.Add(_pub, 0, 2);
        layout.Controls.Add(_host, 0, 3);
        layout.Controls.Add(_port, 0, 4);
        var qrRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        var btnQrFile = new Button { Text = "QR from file…", AutoSize = true };
        var btnQrClip = new Button { Text = "QR from clipboard", AutoSize = true };
        btnQrFile.Click += OnQrFromFile;
        btnQrClip.Click += OnQrFromClipboard;
        qrRow.Controls.Add(btnQrFile);
        qrRow.Controls.Add(btnQrClip);

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Bottom };
        buttons.Controls.Add(_btnOk);
        buttons.Controls.Add(_btnCancel);
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(qrRow, 0, 5);
        layout.Controls.Add(buttons, 0, 6);
        Controls.Add(layout);

        _nick.Dock = DockStyle.Top;
        _id.Dock = DockStyle.Top;
        _pub.Dock = DockStyle.Fill;
        _host.Dock = DockStyle.Top;
        _port.Dock = DockStyle.Top;

        _btnOk.Click += OnOkClicked;
        CancelButton = _btnCancel;
    }

    private void ApplyPayload(PeerQrPayload payload)
    {
        _nick.Text = payload.N;
        _id.Text = payload.Id;
        _pub.Text = payload.K;
        _host.Text = payload.GetCommaSeparatedHosts();
        _port.Text = payload.P.ToString();
    }

    private void OnQrFromFile(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog();
        dlg.Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|All files|*.*";
        dlg.Title = "Image with peer QR code";
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(dlg.FileName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Read QR image file");
            MessageBox.Show(this, ex.Message, "File", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!PeerQrService.TryDecodeImage(bytes, out var payload, out var err))
        {
            _logger.LogWarning("QR decode failed from file {File}: {Error}", dlg.FileName, err);
            MessageBox.Show(this, err ?? "Could not read QR code.", "QR", MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        _userActions.LogInformation("Add chat: QR decoded from file {FileName}", Path.GetFileName(dlg.FileName));
        ApplyPayload(payload);
    }

    private void OnQrFromClipboard(object? sender, EventArgs e)
    {
        if (!Clipboard.ContainsImage())
        {
            MessageBox.Show(this, "Clipboard does not contain an image.", "QR", MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        using var img = Clipboard.GetImage();
        if (img == null)
        {
            MessageBox.Show(this, "Could not read clipboard image.", "QR", MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            img.Save(ms, ImageFormat.Png);
            bytes = ms.ToArray();
        }

        if (!PeerQrService.TryDecodeImage(bytes, out var payload, out var err))
        {
            _logger.LogWarning("QR decode failed from clipboard: {Error}", err);
            MessageBox.Show(this, err ?? "Could not read QR code.", "QR", MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        _userActions.LogInformation("Add chat: QR decoded from clipboard");
        ApplyPayload(payload);
    }

    private void OnOkClicked(object? sender, EventArgs e)
    {
        if (PeerNickname.Length == 0 || PeerNetworkIdShort.Length == 0 || PeerPublicKeyJson.Length == 0 ||
            PeerHosts.Length == 0)
        {
            MessageBox.Show(this, "Fill all fields.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var hosts = PeerHosts.Split(',').Select(s => s.Trim()).ToArray();

        foreach (var host in hosts)
        {
            var isBluetoothMac = BluetoothTransportAddress.TryParseMac(host, out _);
            var isNetworkId = CompressedNetworkId.TryParseShortString(host, out _);
            if (!isBluetoothMac && !isNetworkId && PeerPort is <= 0 or > 65535)
            {
                MessageBox.Show(this, "Invalid peer port.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        _userActions.LogInformation(
            "Add chat: save (peer {Peer}, network id {NetworkId}, host {Host}:{Port})",
            PeerNickname, PeerNetworkIdShort, PeerHosts, PeerPort);
        DialogResult = DialogResult.OK;
        Close();
    }
}
