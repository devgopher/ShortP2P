using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;

namespace ShortP2P.WinForms;

internal sealed class VideoPlayerForm : Form
{
    private readonly byte[] _videoBytes;
    private readonly string _fileName;
    private readonly PictureBox _view = new()
    {
        Dock = DockStyle.Fill,
        SizeMode = PictureBoxSizeMode.Zoom,
        BackColor = Color.Black,
    };
    private readonly System.Windows.Forms.Timer _frameTimer = new();
    private VideoCapture? _capture;
    private MediaPlayer? _mediaPlayer;
    private string? _tempPath;
    private bool _ended;

    public VideoPlayerForm(byte[] videoBytes, string fileName)
    {
        _videoBytes = videoBytes;
        _fileName = string.IsNullOrWhiteSpace(fileName) ? "video.ogv" : Path.GetFileName(fileName);
        Text = "Видео";
        StartPosition = FormStartPosition.CenterParent;
        Width = 760;
        Height = 560;
        Controls.Add(_view);
        _frameTimer.Tick += (_, _) => AdvanceFrame();
        Shown += async (_, _) => await LoadAsync().ConfigureAwait(true);
    }

    private async Task LoadAsync()
    {
        try
        {
            var ext = Path.GetExtension(_fileName);
            if (string.IsNullOrWhiteSpace(ext))
                ext = ".ogv";

            _tempPath = Path.Combine(Path.GetTempPath(), $"shortp2p-video-{Guid.NewGuid():N}{ext}");
            await File.WriteAllBytesAsync(_tempPath, _videoBytes).ConfigureAwait(true);

            _capture = new VideoCapture(_tempPath);
            if (!_capture.IsOpened())
            {
                OpenExternalPlayer();
                return;
            }

            var fps = _capture.Fps;
            if (fps <= 0 || double.IsNaN(fps) || double.IsInfinity(fps))
                fps = 15;
            _frameTimer.Interval = Math.Clamp((int)Math.Round(1000.0 / fps), 15, 200);

            if (await TryStartSystemAudioAsync(_tempPath).ConfigureAwait(true))
            {
                _mediaPlayer!.MediaEnded += (_, _) => BeginInvoke(OnPlaybackEnded);
            }

            if (!ShowFrame())
            {
                OpenExternalPlayer();
                return;
            }

            _frameTimer.Start();
            _mediaPlayer?.Play();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Видео", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
        }
    }

    private async Task<bool> TryStartSystemAudioAsync(string path)
    {
        if (!IsSystemAudioSupported(path))
            return false;

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path).AsTask().ConfigureAwait(true);
            _mediaPlayer = new MediaPlayer();
            _mediaPlayer.Source = MediaSource.CreateFromStorageFile(file);
            return true;
        }
        catch
        {
            _mediaPlayer?.Dispose();
            _mediaPlayer = null;
            return false;
        }
    }

    private static bool IsSystemAudioSupported(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".m4v", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".mov", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".wmv", StringComparison.OrdinalIgnoreCase);
    }

    private void AdvanceFrame()
    {
        if (_capture == null || _ended)
            return;

        if (!ShowFrame())
        {
            _frameTimer.Stop();
            if (_mediaPlayer == null)
                OnPlaybackEnded();
        }
    }

    private bool ShowFrame()
    {
        using var frame = new Mat();
        if (_capture == null || !_capture.Read(frame) || frame.Empty())
            return false;

        using var bitmap = BitmapConverter.ToBitmap(frame);
        var previous = _view.Image;
        _view.Image = (Image)bitmap.Clone();
        previous?.Dispose();
        return true;
    }

    private void OnPlaybackEnded()
    {
        if (_ended)
            return;
        _ended = true;
        _frameTimer.Stop();
        Close();
    }

    private void OpenExternalPlayer()
    {
        _frameTimer.Stop();
        _capture?.Dispose();
        _capture = null;
        _mediaPlayer?.Dispose();
        _mediaPlayer = null;
        _view.Image?.Dispose();
        _view.Image = null;

        if (string.IsNullOrWhiteSpace(_tempPath))
            return;

        Process.Start(new ProcessStartInfo(_tempPath) { UseShellExecute = true });
        Controls.Clear();
        Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Text =
                "Встроенный просмотр недоступен для этого формата.\r\nВидео открыто в проигрывате по умолчанию.",
        });
        Width = 420;
        Height = 140;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _frameTimer.Stop();
        _frameTimer.Dispose();
        _capture?.Dispose();
        _capture = null;
        try
        {
            _mediaPlayer?.Pause();
            _mediaPlayer?.Dispose();
        }
        catch
        {
            // ignore
        }

        _mediaPlayer = null;
        _view.Image?.Dispose();
        _view.Image = null;

        if (!string.IsNullOrWhiteSpace(_tempPath))
        {
            try
            {
                File.Delete(_tempPath);
            }
            catch
            {
                // ignore
            }
        }

        base.OnFormClosed(e);
    }
}
