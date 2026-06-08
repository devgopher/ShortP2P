using System.Diagnostics.CodeAnalysis;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace ShortP2P.Client.ChatMedia;

/// <summary>Сжатие вложений до лимита байт (JPEG, пересчёт размеров).</summary>
public static class ImageAttachmentCompressor
{
    /// <summary>Пытается уложить изображение в <paramref name="maxBytes" />; результат обычно JPEG.</summary>
    public static bool TryCompressToMaxBytes(ReadOnlySpan<byte> source, int maxBytes,
        [NotNullWhen(true)] out byte[]? output,
        [NotNullWhen(false)] out string? error)
    {
        output = null;
        error = null;
        if (maxBytes < 4096)
        {
            error = "Max size is too small.";
            return false;
        }

        try
        {
            int origW;
            int origH;
            using (var probe = Image.Load(source))
            {
                origW = probe.Width;
                origH = probe.Height;
            }

            var scale = 1.0f;
            for (var attempt = 0; attempt < 45; attempt++)
            {
                using var work = Image.Load(source);
                if (scale < 0.999f)
                {
                    var w = Math.Max(1, (int)(origW * scale));
                    var h = Math.Max(1, (int)(origH * scale));
                    work.Mutate(x => x.Resize(w, h));
                }

                var q = Math.Clamp(90 - attempt, 28, 90);
                using var ms = new MemoryStream();
                work.SaveAsJpeg(ms, new JpegEncoder { Quality = q });
                var bytes = ms.ToArray();
                if (bytes.Length <= maxBytes)
                {
                    output = bytes;
                    return true;
                }

                scale *= 0.9f;
                if (origW * scale < 48 || origH * scale < 48)
                    break;
            }

            error = "Cannot compress image to the requested size.";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static string SuggestMimeAfterCompression()
    {
        return "image/jpeg";
    }

    public static bool LooksLikeGif(ReadOnlySpan<byte> header)
    {
        return header.Length >= 6 && header[0] == (byte)'G' && header[1] == (byte)'I' && header[2] == (byte)'F' &&
               header[3] == (byte)'8' && (header[4] == (byte)'7' || header[4] == (byte)'9');
    }
}