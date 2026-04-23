using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShortP2P.WinForms;

internal sealed class CameraRecordForm : Form
{
    private const string RecorderHost = "camera.shortp2p.local";
    private readonly bool _trafficSavingEnabled;
    private readonly int _captureWidth;
    private readonly int _captureHeight;
    private readonly int _audioBitrate;
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    private readonly Label _status = new() { Dock = DockStyle.Top, Height = 26, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button _record = new() { Text = "Record", AutoSize = true };
    private readonly Button _close = new() { Text = "Close", AutoSize = true };
    private readonly System.Windows.Forms.Timer _recordTimer = new() { Interval = 200 };
    private DateTime _recordStartedUtc;
    private bool _recording;
    private string _targetMime = "video/webm";
    private bool _ready;
    private bool _stopping;
    private string? _webRootPath;
    private static readonly JsonSerializerOptions WebMsgJson = new() { PropertyNameCaseInsensitive = true };

    public CameraRecordedVideo? Result { get; private set; }

    public CameraRecordForm(bool trafficSavingEnabled)
    {
        _trafficSavingEnabled = trafficSavingEnabled;
        var resolution = VideoAttachHelper.GetRequiredResolution(_trafficSavingEnabled);
        _captureWidth = resolution.Width;
        _captureHeight = resolution.Height;
        _audioBitrate = _trafficSavingEnabled ? VoiceRecordHelper.TrafficSavingBitrate : VoiceRecordHelper.DefaultBitrate;
        Text = "Камера";
        Width = 820;
        Height = 620;
        StartPosition = FormStartPosition.CenterParent;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8),
        };
        buttons.Controls.Add(_close);
        buttons.Controls.Add(_record);

        Controls.Add(_web);
        Controls.Add(_status);
        Controls.Add(buttons);

        _record.Click += async (_, _) => await ToggleRecordingAsync().ConfigureAwait(true);
        _close.Click += (_, _) => Close();
        _recordTimer.Tick += (_, _) => UpdateRecordingTime();
        Shown += async (_, _) => await StartPreviewAsync().ConfigureAwait(true);
    }

    private async Task StartPreviewAsync()
    {
        try
        {
            await _web.EnsureCoreWebView2Async().ConfigureAwait(true);
            _web.CoreWebView2.PermissionRequested += OnPermissionRequested;
            _web.CoreWebView2.Settings.IsZoomControlEnabled = false;
            _web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _web.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            _webRootPath = Path.Combine(Path.GetTempPath(), $"shortp2p-cam-web-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_webRootPath);
            var htmlPath = Path.Combine(_webRootPath, "recorder.html");
            await File.WriteAllTextAsync(htmlPath, BuildRecorderPageHtml()).ConfigureAwait(true);
            _web.CoreWebView2.SetVirtualHostNameToFolderMapping(RecorderHost, _webRootPath,
                CoreWebView2HostResourceAccessKind.Allow);
            _web.Source = new Uri($"https://{RecorderHost}/recorder.html");
            await Task.Delay(250).ConfigureAwait(true);
            await _web.ExecuteScriptAsync("window.shortp2p.startPreview();").ConfigureAwait(true);
            _ready = true;
            _status.Text =
                $"Готово: {_captureWidth}x{_captureHeight}, audio {_audioBitrate / 1000} kbit/s. Нажмите Record.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Камера", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
        }
    }

    private async Task ToggleRecordingAsync()
    {
        if (!_ready)
            return;
        if (_recording)
        {
            await StopRecordingAsync().ConfigureAwait(true);
            return;
        }

        try
        {
            await _web.ExecuteScriptAsync($"window.shortp2p.startRecording({_audioBitrate});").ConfigureAwait(true);
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
        if (_stopping)
            return;
        _stopping = true;
        _recording = false;
        _recordTimer.Stop();
        _record.Text = "Record";
        try
        {
            await _web.ExecuteScriptAsync("window.shortp2p.stopRecording();").ConfigureAwait(true);
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
            if (_web.CoreWebView2 != null)
            {
                _web.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
                _web.CoreWebView2.PermissionRequested -= OnPermissionRequested;
                _web.CoreWebView2.ClearVirtualHostNameToFolderMapping(RecorderHost);
            }
        }
        catch
        {
            // ignore
        }

        try
        {
            _web.Dispose();
        }
        catch
        {
            // ignore
        }

        if (!string.IsNullOrWhiteSpace(_webRootPath))
        {
            try
            {
                Directory.Delete(_webRootPath, recursive: true);
            }
            catch
            {
                // ignore
            }
        }

        base.OnFormClosed(e);
    }

    private void OnPermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
    {
        if (e.PermissionKind is CoreWebView2PermissionKind.Camera or CoreWebView2PermissionKind.Microphone)
            e.State = CoreWebView2PermissionState.Allow;
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var json = e.TryGetWebMessageAsString();
            var dto = JsonSerializer.Deserialize<WebMsg>(json, WebMsgJson);
            if (dto == null)
                return;
            if (string.Equals(dto.Type, "error", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this, dto.Message ?? "Ошибка камеры/микрофона.", "Камера", MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                _status.Text = "Ошибка доступа к камере или микрофону.";
                return;
            }

            if (!string.Equals(dto.Type, "recorded", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(dto.Base64))
                return;
            var bytes = Convert.FromBase64String(dto.Base64);
            var mime = string.IsNullOrWhiteSpace(dto.Mime) ? _targetMime : dto.Mime.Trim();
            var ext = string.Equals(mime, "video/webm", StringComparison.OrdinalIgnoreCase) ? ".webm" : ".bin";
            Result = new CameraRecordedVideo(bytes, $"camera-{DateTime.UtcNow:yyyyMMdd-HHmmss}{ext}", mime);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Камера", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private string BuildRecorderPageHtml()
    {
        return $$"""
                <!doctype html>
                <html>
                <head>
                  <meta charset="utf-8"/>
                  <style>
                    html,body { margin:0; background:#111; height:100%; }
                    #v { width:100%; height:100%; object-fit:contain; background:#000; }
                  </style>
                </head>
                <body>
                  <video id="v" autoplay muted playsinline></video>
                  <script>
                    const targetWidth = {{_captureWidth}};
                    const targetHeight = {{_captureHeight}};
                    const defaultAudioBps = {{_audioBitrate}};
                    let stream = null;
                    let rec = null;
                    let chunks = [];
                    const v = document.getElementById('v');

                    function post(obj) {
                      window.chrome.webview.postMessage(JSON.stringify(obj));
                    }

                    async function ensureStream() {
                      if (stream) return stream;
                      stream = await navigator.mediaDevices.getUserMedia({
                        video: { width: { ideal: targetWidth }, height: { ideal: targetHeight } },
                        audio: true
                      });
                      v.srcObject = stream;
                      return stream;
                    }

                    window.shortp2p = {
                      async startPreview() {
                        try {
                          await ensureStream();
                        } catch (e) {
                          post({ type:'error', message: e?.message || String(e) });
                        }
                      },
                      async startRecording(audioBps) {
                        try {
                          await ensureStream();
                          chunks = [];
                          const opts = {
                            mimeType: 'video/webm;codecs=vp8,opus',
                            audioBitsPerSecond: Number(audioBps) || defaultAudioBps
                          };
                          rec = new MediaRecorder(stream, opts);
                          rec.ondataavailable = ev => {
                            if (ev.data && ev.data.size > 0) chunks.push(ev.data);
                          };
                          rec.onstop = async () => {
                            try {
                              const blob = new Blob(chunks, { type: 'video/webm' });
                              const buf = await blob.arrayBuffer();
                              const bytes = new Uint8Array(buf);
                              let bin = '';
                              const step = 0x8000;
                              for (let i = 0; i < bytes.length; i += step) {
                                const piece = bytes.subarray(i, i + step);
                                bin += String.fromCharCode.apply(null, piece);
                              }
                              const b64 = btoa(bin);
                              post({ type:'recorded', mime:'video/webm', base64:b64 });
                            } catch (e) {
                              post({ type:'error', message: e?.message || String(e) });
                            }
                          };
                          rec.start();
                        } catch (e) {
                          post({ type:'error', message: e?.message || String(e) });
                        }
                      },
                      stopRecording() {
                        try {
                          if (rec && rec.state !== 'inactive') rec.stop();
                        } catch (e) {
                          post({ type:'error', message: e?.message || String(e) });
                        }
                      }
                    };
                  </script>
                </body>
                </html>
                """;
    }

    private sealed class WebMsg
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("base64")]
        public string? Base64 { get; set; }

        [JsonPropertyName("mime")]
        public string? Mime { get; set; }
    }
}

internal sealed record CameraRecordedVideo(byte[] Bytes, string FileName, string MimeType);
