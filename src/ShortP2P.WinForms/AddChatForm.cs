using System.Drawing;
using System.Drawing.Imaging;
using ShortP2P.Client.Qr;

namespace ShortP2P.WinForms;

public sealed class AddChatForm : Form
{
    private readonly TextBox _nick = new() { PlaceholderText = "Peer nickname" };
    private readonly TextBox _id = new() { PlaceholderText = "Peer network id" };
    private readonly TextBox _pub = new() { PlaceholderText = "Peer RSA public key JSON", Multiline = true, Height = 80, ScrollBars = ScrollBars.Vertical };
    private readonly TextBox _host = new() { PlaceholderText = "Peer IP / host" };
    private readonly TextBox _port = new() { PlaceholderText = "UDP port" };
    private readonly Button _btnOk = new() { Text = "Save" };
    private readonly Button _btnCancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel };

    public string PeerNickname => _nick.Text.Trim();
    public string PeerNetworkIdShort => _id.Text.Trim();
    public string PeerPublicKeyJson => _pub.Text.Trim();
    public string PeerHost => _host.Text.Trim();
    public int PeerPort => int.TryParse(_port.Text, out var p) ? p : 0;

    public AddChatForm()
    {
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
        _host.Text = payload.H;
        _port.Text = payload.P.ToString();
    }

    private void OnQrFromFile(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|All files|*.*",
            Title = "Image with peer QR code",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(dlg.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "File", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!PeerQrService.TryDecodeImage(bytes, out var payload, out var err))
        {
            MessageBox.Show(this, err ?? "Could not read QR code.", "QR", MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

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
            MessageBox.Show(this, err ?? "Could not read QR code.", "QR", MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        ApplyPayload(payload);
    }

    private void OnOkClicked(object? sender, EventArgs e)
    {
        if (PeerNickname.Length == 0 || PeerNetworkIdShort.Length == 0 || PeerPublicKeyJson.Length == 0 ||
            PeerHost.Length == 0)
        {
            MessageBox.Show(this, "Fill all fields.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (PeerPort <= 0 || PeerPort > 65535)
        {
            MessageBox.Show(this, "Invalid peer port.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
