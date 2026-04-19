namespace ShortP2P.WinForms;

/// <summary>Просмотр изображения из чата.</summary>
public sealed class ImageViewForm : Form
{
    public ImageViewForm(string title, byte[] imageBytes)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        Width = 520;
        Height = 420;
        var pic = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            Image = Image.FromStream(new MemoryStream(imageBytes)),
        };
        FormClosed += (_, _) => pic.Image?.Dispose();
        Controls.Add(pic);
    }
}
