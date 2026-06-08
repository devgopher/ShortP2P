using Windows.Devices.Enumeration;
using NAudio.Wave;
using ShortP2P.Auth;

namespace ShortP2P.WinForms;

public sealed class AppSettingsStore(ISessionStorage storage)
{
    private const string KVoiceInputDeviceNumber = "wf_voice_input_device_number";
    private const string KTrafficSavingEnabled = "wf_traffic_saving_enabled";
    private const string KVideoInputDeviceId = "wf_video_input_device_id";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ISessionStorage _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    private volatile bool _loaded;

    public AppSettingsSnapshot Current { get; private set; } = new(null, false, null);

    public async Task InitializeAsync()
    {
        await EnsureLoadedAsync().ConfigureAwait(false);
    }

    public async Task<AppSettingsSnapshot> GetAsync()
    {
        await EnsureLoadedAsync().ConfigureAwait(false);
        return Current;
    }

    public async Task SetVoiceInputDeviceNumberAsync(int? deviceNumber)
    {
        if (deviceNumber is < 0)
            throw new ArgumentOutOfRangeException(nameof(deviceNumber));

        await EnsureLoadedAsync().ConfigureAwait(false);
        if (deviceNumber.HasValue)
            await _storage.SetAsync(KVoiceInputDeviceNumber, deviceNumber.Value.ToString()).ConfigureAwait(false);
        else
            _storage.Remove(KVoiceInputDeviceNumber);

        Current = Current with { VoiceInputDeviceNumber = deviceNumber };
    }

    public async Task SetTrafficSavingEnabledAsync(bool enabled)
    {
        await EnsureLoadedAsync().ConfigureAwait(false);
        await _storage.SetAsync(KTrafficSavingEnabled, enabled.ToString()).ConfigureAwait(false);
        Current = Current with { TrafficSavingEnabled = enabled };
    }

    public async Task SetVideoInputDeviceIdAsync(string? deviceId)
    {
        await EnsureLoadedAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            _storage.Remove(KVideoInputDeviceId);
            Current = Current with { VideoInputDeviceId = null };
            return;
        }

        await _storage.SetAsync(KVideoInputDeviceId, deviceId.Trim()).ConfigureAwait(false);
        Current = Current with { VideoInputDeviceId = deviceId.Trim() };
    }

    private async Task EnsureLoadedAsync()
    {
        if (_loaded)
            return;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_loaded)
                return;
            int? saved = null;
            var raw = await _storage.GetAsync(KVoiceInputDeviceNumber).ConfigureAwait(false);
            if (int.TryParse(raw, out var n) && n >= 0)
                saved = n;
            var trafficSavingEnabled =
                bool.TryParse(await _storage.GetAsync(KTrafficSavingEnabled).ConfigureAwait(false), out var ts) && ts;
            var videoDeviceId = await _storage.GetAsync(KVideoInputDeviceId).ConfigureAwait(false);
            Current = new AppSettingsSnapshot(saved, trafficSavingEnabled,
                string.IsNullOrWhiteSpace(videoDeviceId) ? null : videoDeviceId.Trim());
            _loaded = true;
        }
        finally
        {
            _gate.Release();
        }
    }
}

public sealed record AppSettingsSnapshot(
    int? VoiceInputDeviceNumber,
    bool TrafficSavingEnabled,
    string? VideoInputDeviceId);

public static class AudioInputDeviceCatalog
{
    public static IReadOnlyList<AudioInputDeviceInfo> GetAll()
    {
        var result = new List<AudioInputDeviceInfo>();
        var count = WaveIn.DeviceCount;
        for (var i = 0; i < count; i++)
        {
            var caps = WaveIn.GetCapabilities(i);
            var channels = caps.Channels;
            var label = channels > 0
                ? $"{caps.ProductName} ({channels} ch)"
                : caps.ProductName;
            result.Add(new AudioInputDeviceInfo(i, label));
        }

        return result;
    }
}

public sealed record AudioInputDeviceInfo(int DeviceNumber, string DisplayName);

public static class VideoInputDeviceCatalog
{
    public static async Task<IReadOnlyList<VideoInputDeviceInfo>> GetAllAsync()
    {
        var list = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
        return list.Select(d => new VideoInputDeviceInfo(d.Id, d.Name)).ToList();
    }
}

public sealed record VideoInputDeviceInfo(string DeviceId, string DisplayName);