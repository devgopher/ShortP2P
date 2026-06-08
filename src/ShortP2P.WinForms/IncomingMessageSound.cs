using NAudio.Vorbis;
using NAudio.Wave;

namespace ShortP2P.WinForms;

internal static class IncomingMessageSound
{
    public static void TryPlay()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Sounds", "GChord.ogg");
        _ = Task.Run(() => PlaySync(path));
    }

    private static void PlaySync(string path)
    {
        try
        {
            if (!File.Exists(path))
                return;
            using var reader = new VorbisWaveReader(path);
            using var output = new WaveOutEvent();
            using var done = new ManualResetEventSlim(false);
            output.PlaybackStopped += (_, _) => done.Set();
            output.Init(reader);
            output.Play();
            done.Wait(TimeSpan.FromSeconds(30));
        }
        catch
        {
            // ignore: missing codec, no audio device, etc.
        }
    }
}