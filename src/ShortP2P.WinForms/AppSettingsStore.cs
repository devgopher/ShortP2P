using NAudio.Wave;
using ShortP2P.Client;

namespace ShortP2P.WinForms;

public sealed class AppSettingsStore(ISessionStorage storage)
{
    private const string KVoiceInputDeviceNumber = "wf_voice_input_device_number";
    private readonly ISessionStorage _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AppSettingsSnapshot _current = new(null);
    private volatile bool _loaded;

    public AppSettingsSnapshot Current => _current;

    public async Task InitializeAsync()
    {
        await EnsureLoadedAsync().ConfigureAwait(false);
    }

    public async Task<AppSettingsSnapshot> GetAsync()
    {
        await EnsureLoadedAsync().ConfigureAwait(false);
        return _current;
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

        _current = _current with { VoiceInputDeviceNumber = deviceNumber };
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
            _current = new AppSettingsSnapshot(saved);
            _loaded = true;
        }
        finally
        {
            _gate.Release();
        }
    }
}

public sealed record AppSettingsSnapshot(int? VoiceInputDeviceNumber);

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
