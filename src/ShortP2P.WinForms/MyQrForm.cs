using System.Drawing;
using ShortP2P.Client.Qr;
using ShortP2P.Client.Services;

namespace ShortP2P.WinForms;

public sealed class MyQrForm : Form
{
    public MyQrForm(AuthService auth)
    {
        Text = "My QR code";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Width = 400;
        Height = 460;
        Padding = new Padding(12);

        var u = auth.CurrentUser;
        if (u == null)
        {
            Controls.Add(new Label { Text = "Not logged in.", AutoSize = true, Dock = DockStyle.Fill });
            return;
        }

        var hint = new Label
        {
            Text =
                "Show this code to a peer so they can add you. The IP is guessed from this PC; they may need to fix the host if you use VPN or multiple NICs.",
            AutoSize = true,
            MaximumSize = new Size(360, 0),
            Dock = DockStyle.Top,
        };

        var pub = RsaKeySerializer.SerializePublic(auth.GetCurrentPublicKey());
        var png = PeerQrService.EncodeQrPng(PeerQrService.BuildPayload(u, pub));

        var picture = new PictureBox
        {
            Size = new Size(280, 280),
            SizeMode = PictureBoxSizeMode.Zoom,
            BorderStyle = BorderStyle.FixedSingle,
            Image = new Bitmap(new MemoryStream(png)),
        };

        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
        };
        panel.Controls.Add(hint);
        panel.Controls.Add(picture);
        Controls.Add(panel);
    }
}
