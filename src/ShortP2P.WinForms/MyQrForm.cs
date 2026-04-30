using System.Drawing;
using Microsoft.Extensions.Logging;
using ShortP2P.Auth;
using ShortP2P.Client.Qr;
using ShortP2P.Crypto;
using ShortP2P.Client.Services;
using ShortP2P.Transport.Bluetooth.Windows;

namespace ShortP2P.WinForms;

public sealed class MyQrForm : Form
{
    public MyQrForm(AuthService auth, ILogger<MyQrForm> log, ILogger<UserAction> userActions)
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
            log.LogWarning("My QR opened but user is not logged in");
            Controls.Add(new Label { Text = "Not logged in.", AutoSize = true, Dock = DockStyle.Fill });
            return;
        }

        var hint = new Label
        {
            Text =
                "Show this code to a peer so they can add you. All detected IPv4 addresses on this PC are included (best first); the peer can edit the list after scanning if needed.",
            AutoSize = true,
            MaximumSize = new Size(360, 0),
            Dock = DockStyle.Top,
        };

        var pub = RsaKeySerializer.SerializePublic(auth.GetCurrentPublicKey());
        string? btMac = null;
        try
        {
            btMac = LocalAdapterBluetoothMac.TryGetAdapterMacStringAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // no Bluetooth / WinRT
        }

        var png = PeerQrService.EncodeQrPng(PeerQrService.BuildPayload(u, pub, null, btMac, null));

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

        userActions.LogInformation("My QR: opened (user {Nickname}, network id {NetworkId})",
            u.Nickname, u.NetworkIdShort);
    }
}
