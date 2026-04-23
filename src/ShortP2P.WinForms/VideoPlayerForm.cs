using Microsoft.Web.WebView2.WinForms;

namespace ShortP2P.WinForms;

internal sealed class VideoPlayerForm : Form
{
    private readonly byte[] _videoBytes;
    private readonly string _fileName;
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    private string? _tempPath;

    public VideoPlayerForm(byte[] videoBytes, string fileName)
    {
        _videoBytes = videoBytes;
        _fileName = string.IsNullOrWhiteSpace(fileName) ? "video.ogv" : Path.GetFileName(fileName);
        Text = "Видео";
        StartPosition = FormStartPosition.CenterParent;
        Width = 760;
        Height = 560;
        Controls.Add(_web);
        Shown += async (_, _) => await LoadVideoAsync().ConfigureAwait(true);
    }

    private async Task LoadVideoAsync()
    {
        try
        {
            _tempPath = Path.Combine(Path.GetTempPath(), $"shortp2p-video-{Guid.NewGuid():N}.ogv");
            await File.WriteAllBytesAsync(_tempPath, _videoBytes).ConfigureAwait(true);
            await _web.EnsureCoreWebView2Async().ConfigureAwait(true);
            var videoUri = new Uri(_tempPath).AbsoluteUri;
            var html =
                "<!doctype html><html><head><meta charset=\"utf-8\"></head><body style=\"margin:0;background:#111;display:flex;justify-content:center;align-items:center;height:100vh;\">" +
                $"<video controls autoplay style=\"max-width:100%;max-height:100%;\" src=\"{videoUri}\"></video>" +
                "</body></html>";
            _web.NavigateToString(html);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Видео", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        try
        {
            _web.Dispose();
        }
        catch
        {
            // ignore
        }

        if (string.IsNullOrWhiteSpace(_tempPath))
            return;
        try
        {
            File.Delete(_tempPath);
        }
        catch
        {
            // ignore
        }
    }
}
