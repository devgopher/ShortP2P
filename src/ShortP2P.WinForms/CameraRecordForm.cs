using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;
using Windows.Storage;
using Timer = System.Windows.Forms.Timer;

namespace ShortP2P.WinForms;

internal sealed class CameraRecordForm : Form
{
    private readonly int _captureHeight;
    private readonly int _captureWidth;
    private readonly Button _close = new() { Text = "Close", AutoSize = true };
    private readonly PictureBox _preview = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom };
    private readonly Button _record = new() { Text = "Record", AutoSize = true };
    private readonly Timer _recordTimer = new() { Interval = 200 };

    private readonly Label _status = new()
        { Dock = DockStyle.Top, Height = 26, TextAlign = ContentAlignment.MiddleLeft };

    private readonly bool _trafficSavingEnabled;
    private readonly string? _videoDeviceId;
    private MediaCapture? _capture;
    private MediaFrameReader? _frameReader;
    private bool _ready;
    private StorageFile? _recordFile;
    private bool _recording;
    private DateTime _recordStartedUtc;
    private string? _recordTempPath;
    private bool _stopping;

    public CameraRecordForm(bool trafficSavingEnabled, string? videoDeviceId)
    {
        _trafficSavingEnabled = trafficSavingEnabled;
        _videoDeviceId = string.IsNullOrWhiteSpace(videoDeviceId) ? null : videoDeviceId.Trim();
        var resolution = VideoAttachHelper.GetRequiredResolution(_trafficSavingEnabled);
        _captureWidth = resolution.Width;
        _captureHeight = resolution.Height;
        Text = "Камера";
        Width = 820;
        Height = 620;
        StartPosition = FormStartPosition.CenterParent;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8)
        };
        buttons.Controls.Add(_close);
        buttons.Controls.Add(_record);

        Controls.Add(_preview);
        Controls.Add(_status);
        Controls.Add(buttons);

        _record.Click += async (_, _) => await ToggleRecordingAsync().ConfigureAwait(true);
        _close.Click += (_, _) => Close();
        _recordTimer.Tick += (_, _) => UpdateRecordingTime();
        Shown += async (_, _) => await StartPreviewAsync().ConfigureAwait(true);
    }

    public CameraRecordedVideo? Result { get; private set; }

    private async Task StartPreviewAsync()
    {
        try
        {
            _capture = new MediaCapture();
            var settings = new MediaCaptureInitializationSettings
            {
                StreamingCaptureMode = StreamingCaptureMode.AudioAndVideo,
                MediaCategory = MediaCategory.Communications,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu
            };
            if (!string.IsNullOrWhiteSpace(_videoDeviceId))
                settings.VideoDeviceId = _videoDeviceId;

            await _capture.InitializeAsync(settings).AsTask().ConfigureAwait(true);
            var frameSource = FindPreviewFrameSource(_capture);
            if (frameSource == null)
                throw new InvalidOperationException("Камера не предоставила видеопоток для предпросмотра.");

            _frameReader = await _capture.CreateFrameReaderAsync(frameSource).AsTask().ConfigureAwait(true);
            _frameReader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;
            _frameReader.FrameArrived += OnPreviewFrameArrived;
            await _frameReader.StartAsync().AsTask().ConfigureAwait(true);

            _ready = true;
            var audioKbps = (_trafficSavingEnabled
                ? VoiceRecordHelper.TrafficSavingBitrate
                : VoiceRecordHelper.DefaultBitrate) / 1000;
            _status.Text =
                $"Готово: {_captureWidth}x{_captureHeight}, audio ~{audioKbps} kbit/s (MP4). Нажмите Record.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Камера", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
        }
    }

    private static MediaFrameSource? FindPreviewFrameSource(MediaCapture capture)
    {
        foreach (var source in capture.FrameSources.Values)
            if (source.Info.MediaStreamType == MediaStreamType.VideoPreview)
                return source;

        foreach (var source in capture.FrameSources.Values)
            if (source.Info.MediaStreamType == MediaStreamType.VideoRecord)
                return source;

        foreach (var source in capture.FrameSources.Values)
            if (source.Info.SourceKind == MediaFrameSourceKind.Color)
                return source;

        return capture.FrameSources.Values.FirstOrDefault();
    }

    private void OnPreviewFrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        if (IsDisposed || !IsHandleCreated)
            return;

        using var frame = sender.TryAcquireLatestFrame();
        var softwareBitmap = frame?.VideoMediaFrame?.SoftwareBitmap;
        if (softwareBitmap == null)
            return;

        try
        {
            using var bitmap = SoftwareBitmapToBitmap(softwareBitmap);
            BeginInvoke(() =>
            {
                if (IsDisposed)
                    return;
                var old = _preview.Image;
                _preview.Image = (Image)bitmap.Clone();
                old?.Dispose();
            });
        }
        catch
        {
            // ignore preview glitches
        }
    }

    private static Bitmap SoftwareBitmapToBitmap(SoftwareBitmap softwareBitmap)
    {
        using var converted =
            SoftwareBitmap.Convert(softwareBitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        var bytes = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyToBuffer(bytes.AsBuffer());
        var bitmap = new Bitmap(converted.PixelWidth, converted.PixelHeight, PixelFormat.Format32bppArgb);
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.WriteOnly, bitmap.PixelFormat);
        try
        {
            Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }

    private async Task ToggleRecordingAsync()
    {
        if (!_ready || _capture == null)
            return;
        if (_recording)
        {
            await StopRecordingAsync().ConfigureAwait(true);
            return;
        }

        try
        {
            var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetTempPath()).AsTask().ConfigureAwait(true);
            _recordFile = await folder
                .CreateFileAsync($"shortp2p-cam-{Guid.NewGuid():N}.mp4", CreationCollisionOption.ReplaceExisting)
                .AsTask()
                .ConfigureAwait(true);
            _recordTempPath = _recordFile.Path;
            var profile = CreateEncodingProfile();
            await _capture.StartRecordToStorageFileAsync(profile, _recordFile).AsTask().ConfigureAwait(true);
            _recording = true;
            _recordStartedUtc = DateTime.UtcNow;
            _record.Text = "Stop";
            _status.Text = "Идет запись...";
            _recordTimer.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Камера", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private MediaEncodingProfile CreateEncodingProfile()
    {
        var audioBitrate = (uint)(_trafficSavingEnabled
            ? VoiceRecordHelper.TrafficSavingBitrate
            : VoiceRecordHelper.DefaultBitrate);
        var videoBitrate = (uint)(_trafficSavingEnabled ? 250_000 : 700_000);
        var template = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Wvga);
        var profile = new MediaEncodingProfile
        {
            Audio = AudioEncodingProperties.CreateAac(audioBitrate, 1, 48_000),
            Video = VideoEncodingProperties.CreateH264(),
            Container = template.Container
        };
        profile.Video.Width = (uint)_captureWidth;
        profile.Video.Height = (uint)_captureHeight;
        profile.Video.Bitrate = videoBitrate;
        profile.Video.FrameRate.Numerator = 15;
        profile.Video.FrameRate.Denominator = 1;
        return profile;
    }

    private void UpdateRecordingTime()
    {
        if (!_recording)
            return;
        var elapsed = DateTime.UtcNow - _recordStartedUtc;
        var max = VideoAttachHelper.MaxDurationSeconds;
        _status.Text = $"Идет запись... {Math.Ceiling(elapsed.TotalSeconds)}/{max} сек";
        if (elapsed.TotalSeconds >= max)
            _ = StopRecordingAsync();
    }

    private async Task StopRecordingAsync()
    {
        if (_stopping || _capture == null || !_recording)
            return;
        _stopping = true;
        _recording = false;
        _recordTimer.Stop();
        _record.Text = "Record";
        try
        {
            await _capture.StopRecordAsync().AsTask().ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(_recordTempPath) || !File.Exists(_recordTempPath))
                throw new InvalidOperationException("Файл записи не найден.");

            var bytes = await File.ReadAllBytesAsync(_recordTempPath).ConfigureAwait(true);
            if (bytes.Length == 0)
                throw new InvalidOperationException("Запись пуста.");

            Result = new CameraRecordedVideo(bytes, $"camera-{DateTime.UtcNow:yyyyMMdd-HHmmss}.mp4", "video/mp4");
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Камера", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _status.Text = "Ошибка сохранения записи.";
        }
        finally
        {
            _stopping = false;
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _recordTimer.Stop();
        try
        {
            if (_recording && _capture != null)
                _ = _capture.StopRecordAsync().AsTask();
        }
        catch
        {
            // ignore
        }

        try
        {
            if (_frameReader != null)
            {
                _frameReader.FrameArrived -= OnPreviewFrameArrived;
                _ = _frameReader.StopAsync().AsTask();
                _frameReader.Dispose();
            }
        }
        catch
        {
            // ignore
        }

        try
        {
            _capture?.Dispose();
        }
        catch
        {
            // ignore
        }

        _preview.Image?.Dispose();

        if (!string.IsNullOrWhiteSpace(_recordTempPath))
            try
            {
                File.Delete(_recordTempPath);
            }
            catch
            {
                // ignore
            }

        base.OnFormClosed(e);
    }
}

internal sealed record CameraRecordedVideo(byte[] Bytes, string FileName, string MimeType);