using Concentus;
using Concentus.Enums;
using Concentus.Oggfile;
using NAudio.Wave;
using ShortP2P.Discovery;

namespace ShortP2P.WinForms;

/// <summary>Голосовые сообщения: Ogg + Opus, моно, без ffmpeg.</summary>
internal static class VoiceRecordHelper
{
    public const string VoiceMessageMime = "audio/ogg";
    public const string VoiceFileName = "voice.ogg";

    private const int OpusDecodeSampleRate = 48000;

    public const int UltraEconomyBitrate = TrafficQualityModeExtensions.UltraEconomyVoiceBitrate;
    public const int TrafficSavingBitrate = TrafficQualityModeExtensions.EconomyVoiceBitrate;
    public const int DefaultBitrate = TrafficQualityModeExtensions.NormalVoiceBitrate;

    /// <summary>WAV (RIFF) в памяти → Ogg Opus mono (битрейт по режиму качества).</summary>
    public static Task<(bool Ok, byte[]? OggBytes, string? Error)> EncodeWavPcmToOggOpusAsync(byte[] wavBytes,
        TrafficQualityMode trafficQuality, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => EncodeWavPcmToOggOpus(wavBytes, trafficQuality), cancellationToken);
    }

    private static (bool Ok, byte[]? OggBytes, string? Error) EncodeWavPcmToOggOpus(byte[] wavBytes,
        TrafficQualityMode trafficQuality)
    {
        if (wavBytes.Length < 44)
            return (false, null, "Слишком короткая запись.");

        try
        {
            using var wavMs = new MemoryStream(wavBytes, false);
            using var wav = new WaveFileReader(wavMs);
            var fmt = wav.WaveFormat;
            if (fmt.BitsPerSample != 16)
                return (false, null, "Ожидается PCM 16 bit в WAV.");

            var sampleRate = fmt.SampleRate;
            var channels = fmt.Channels;
            var byteBuffer = new byte[wav.Length];
            var pos = 0;
            int read;
            while ((read = wav.Read(byteBuffer, pos, byteBuffer.Length - pos)) > 0)
                pos += read;
            if (pos != byteBuffer.Length)
                Array.Resize(ref byteBuffer, pos);

            var frameCount = byteBuffer.Length / (2 * channels);
            if (frameCount == 0)
                return (false, null, "Нет аудиосэмплов.");

            var interleavedShorts = new short[frameCount * channels];
            Buffer.BlockCopy(byteBuffer, 0, interleavedShorts, 0, byteBuffer.Length);

            short[] monoPcm;
            if (channels == 1)
            {
                monoPcm = new short[frameCount];
                Array.Copy(interleavedShorts, monoPcm, frameCount);
            }
            else
            {
                monoPcm = new short[frameCount];
                for (var i = 0; i < frameCount; i++)
                {
                    var sum = 0;
                    for (var c = 0; c < channels; c++)
                        sum += interleavedShorts[i * channels + c];
                    monoPcm[i] = (short)(sum / channels);
                }
            }

            var oggMs = new MemoryStream();
            var encoder =
                OpusCodecFactory.CreateEncoder(OpusDecodeSampleRate, 1, OpusApplication.OPUS_APPLICATION_VOIP);
            encoder.Bitrate = trafficQuality.GetVoiceBitrate();
            var tags = new OpusTags();
            var oggOut = new OpusOggWriteStream(encoder, oggMs, tags, sampleRate);
            oggOut.WriteSamples(monoPcm, 0, monoPcm.Length);
            oggOut.Finish();

            var ogg = oggMs.ToArray();
            return ogg.Length == 0 ? (false, null, "Пустой выход кодера.") : (true, ogg, null);
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }

    /// <summary>Декодирование Ogg Opus → PCM16 моно для NAudio.</summary>
    public static (byte[] PcmBytes, int SampleRateHz) DecodeOpusOggToPcm16(byte[] oggBytes)
    {
        var mem = new MemoryStream(oggBytes, false);
        var decoder = OpusCodecFactory.CreateDecoder(OpusDecodeSampleRate, 1);
        var oggIn = new OpusOggReadStream(decoder, mem);
        var samples = new List<short>();
        while (oggIn.HasNextPacket)
        {
            var pkt = oggIn.DecodeNextPacket();
            if (pkt is { Length: > 0 })
                samples.AddRange(pkt);
        }

        if (samples.Count == 0)
            throw new InvalidOperationException("Пустой Opus поток.");

        var pcm = new byte[samples.Count * 2];
        Buffer.BlockCopy(samples.ToArray(), 0, pcm, 0, pcm.Length);
        return (pcm, OpusDecodeSampleRate);
    }
}
