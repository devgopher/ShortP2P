using ShortP2P.Client.Qr;

namespace ShortP2P.WinForms;

public sealed class MessengerServerQrForm : Form
{
    private readonly byte[] _qrPng;
    private readonly string _caption;

    public MessengerServerQrForm(MessengerServerQrPayload payload, byte[] qrPng)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(qrPng);

        _qrPng = qrPng;
        _caption = $"{payload.H}:{payload.P}";
        Text = "Поделиться сервером";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Width = 400;
        Height = 460;
        Padding = new Padding(12);

        var hint = new Label
        {
            Text = $"QR-код сервера {_caption}. Другой клиент может импортировать этот файл.",
            AutoSize = true,
            MaximumSize = new Size(360, 0),
            Dock = DockStyle.Top
        };

        var picture = new PictureBox
        {
            Size = new Size(280, 280),
            SizeMode = PictureBoxSizeMode.Zoom,
            BorderStyle = BorderStyle.FixedSingle
        };
        using (var ms = new MemoryStream(_qrPng))
        using (var decoded = Image.FromStream(ms))
            picture.Image = new Bitmap(decoded);

        var btnSave = new Button { Text = "Сохранить PNG…", AutoSize = true };
        btnSave.Click += (_, _) => SavePng();
        var btnClose = new Button { Text = "Закрыть", AutoSize = true, DialogResult = DialogResult.OK };

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight
        };
        buttons.Controls.Add(btnSave);
        buttons.Controls.Add(btnClose);

        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true
        };
        panel.Controls.Add(hint);
        panel.Controls.Add(picture);
        panel.Controls.Add(buttons);
        Controls.Add(panel);
        AcceptButton = btnClose;
        CancelButton = btnClose;
    }

    private void SavePng()
    {
        using var dlg = new SaveFileDialog
        {
            Title = "Сохранить QR-код сервера",
            Filter = "PNG|*.png",
            FileName = "shortp2p-server-qr.png"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            File.WriteAllBytes(dlg.FileName, _qrPng);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Сохранить PNG", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
