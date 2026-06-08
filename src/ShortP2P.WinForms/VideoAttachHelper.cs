namespace ShortP2P.WinForms;

internal static class VideoAttachHelper
{
    public const string VideoMessageMime = "video/ogg";
    public const string VideoMessageExtension = ".ogv";
    public const int NormalVideoWidth = 320;
    public const int NormalVideoHeight = 240;
    public const int TrafficSavingVideoWidth = 160;
    public const int TrafficSavingVideoHeight = 120;
    public const int MaxDurationSeconds = 60;

    public static bool TryGetMimeFromExtension(string filePath, out string mime)
    {
        mime = string.Equals(Path.GetExtension(filePath), VideoMessageExtension, StringComparison.OrdinalIgnoreCase)
            ? VideoMessageMime
            : "";
        return mime.Length > 0;
    }

    public static async Task<(bool Success, byte[]? Bytes, string? OutputFileName, string? OutputMime, string? Error)>
        TryLoadAndValidateOgvAsync(string inputPath, int maxBytes, bool trafficSavingEnabled,
            CancellationToken cancellationToken = default)
    {
        if (!File.Exists(inputPath))
            return (false, null, null, null, "Файл не найден.");

        if (!TryGetMimeFromExtension(inputPath, out var mime))
            return (false, null, null, null, $"Допустим только {VideoMessageExtension}.");

        VideoMeta meta;
        try
        {
            meta = ReadMeta(inputPath);
        }
        catch (Exception ex)
        {
            return (false, null, null, null, $"Не удалось прочитать метаданные OGV: {ex.Message}");
        }

        if (meta.DurationSeconds <= 0 || meta.DurationSeconds > MaxDurationSeconds)
            return (false, null, null, null, $"Длительность должна быть от 1 до {MaxDurationSeconds} секунд.");
        var (expectedW, expectedH) = GetRequiredResolution(trafficSavingEnabled);
        if (meta.Width != expectedW || meta.Height != expectedH)
            return (false, null, null, null, $"Разрешение должно быть ровно {expectedW}x{expectedH}.");

        var bytes = await File.ReadAllBytesAsync(inputPath, cancellationToken).ConfigureAwait(false);
        if (bytes.Length <= 0)
            return (false, null, null, null, "Файл пустой.");
        if (bytes.Length > maxBytes)
            return (false, null, null, null, $"Файл больше лимита {(maxBytes + 1048575) / 1048576} МБ.");
        return (true, bytes, Path.GetFileName(inputPath), mime, null);
    }

    public static (int Width, int Height) GetRequiredResolution(bool trafficSavingEnabled)
    {
        return trafficSavingEnabled
            ? (TrafficSavingVideoWidth, TrafficSavingVideoHeight)
            : (NormalVideoWidth, NormalVideoHeight);
    }

    private static VideoMeta ReadMeta(string filePath)
    {
        using var file = TagLib.File.Create(filePath);
        var props = file.Properties;
        var durationSeconds = props.Duration.TotalSeconds;
        var width = props.VideoWidth;
        var height = props.VideoHeight;
        return new VideoMeta(durationSeconds, width, height);
    }

    private sealed record VideoMeta(double DurationSeconds, int Width, int Height);
}