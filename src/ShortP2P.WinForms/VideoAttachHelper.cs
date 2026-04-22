using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;

namespace ShortP2P.WinForms;

internal static class VideoAttachHelper
{
    private static readonly int[] CompressionQpSteps = [24, 28, 32, 36];
    private static readonly int[] DownscaleHeights = [720, 480, 360, 240, 144];
    private const string FfmpegDownloadUrl =
        "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-lgpl.zip";
    private static string FfmpegExePath => Path.Combine(AppContext.BaseDirectory, "ffmpeg", "ffmpeg.exe");
    private static string FfprobeExePath => Path.Combine(AppContext.BaseDirectory, "ffmpeg", "ffprobe.exe");
    private static string FfmpegFolderPath => Path.Combine(AppContext.BaseDirectory, "ffmpeg");

    /// <summary>Только ffmpeg (достаточно для кодирования голоса в Ogg).</summary>
    public static string BundledFfmpegExecutable => FfmpegExePath;

    public static bool IsBundledFfmpegAvailable() => File.Exists(FfmpegExePath);

    public static bool AreBundledToolsAvailable() => File.Exists(FfmpegExePath) && File.Exists(FfprobeExePath);

    public static async Task<(bool Success, string? Error)> TryDownloadBundledToolsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            Directory.CreateDirectory(FfmpegFolderPath);
            var archivePath = Path.Combine(Path.GetTempPath(), $"shortp2p-ffmpeg-{Guid.NewGuid():N}.zip");
            try
            {
                using var http = new HttpClient();
                await using (var stream = await http.GetStreamAsync(FfmpegDownloadUrl, cancellationToken).ConfigureAwait(false))
                await using (var fs = new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None))
                    await stream.CopyToAsync(fs, cancellationToken).ConfigureAwait(false);

                using var zip = ZipFile.OpenRead(archivePath);
                var ffmpegEntry = zip.Entries.FirstOrDefault(e =>
                    e.FullName.EndsWith("/bin/ffmpeg.exe", StringComparison.OrdinalIgnoreCase));
                var ffprobeEntry = zip.Entries.FirstOrDefault(e =>
                    e.FullName.EndsWith("/bin/ffprobe.exe", StringComparison.OrdinalIgnoreCase));
                if (ffmpegEntry == null || ffprobeEntry == null)
                    return (false, "ffmpeg/ffprobe not found in downloaded archive.");

                ffmpegEntry.ExtractToFile(FfmpegExePath, overwrite: true);
                ffprobeEntry.ExtractToFile(FfprobeExePath, overwrite: true);
            }
            finally
            {
                try
                {
                    if (File.Exists(archivePath))
                        File.Delete(archivePath);
                }
                catch
                {
                    // ignore temp cleanup failures
                }
            }

            return AreBundledToolsAvailable()
                ? (true, null)
                : (false, "Download completed, but tools are not available.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public static bool TryGetMimeFromExtension(string filePath, out string mime)
    {
        mime = Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".mp4" => "video/mp4",
            ".avi" => "video/x-msvideo",
            ".mov" => "video/quicktime",
            ".wmv" => "video/x-ms-wmv",
            ".ogv" => "video/ogg",
            ".webm" => "video/webm",
            _ => "",
        };
        return mime.Length > 0;
    }

    public static async Task<(bool Success, double DurationSeconds, string? Error)> TryProbeDurationSecondsAsync(
        string inputPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(FfprobeExePath))
            return (false, 0, "ffprobe not bundled.");

        var args = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{inputPath}\"";
        var (exitCode, stdOut, stdErr) = await RunToolAsync(FfprobeExePath, args, cancellationToken).ConfigureAwait(false);
        if (exitCode != 0)
            return (false, 0, string.IsNullOrWhiteSpace(stdErr) ? "ffprobe failed." : stdErr.Trim());
        var raw = stdOut.Trim();
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) || seconds <= 0)
            return (false, 0, "Could not parse video duration.");
        return (true, seconds, null);
    }

    public static async Task<(bool Success, byte[]? Bytes, string? OutputFileName, string? OutputMime, string? Error)>
        TryCompressToLimitAsync(string inputPath, int maxBytes, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(FfmpegExePath))
            return (false, null, null, null, "ffmpeg not bundled.");

        var tempDir = Path.Combine(Path.GetTempPath(), $"shortp2p-video-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var pass1 = await TryRunVariantsAsync(inputPath, maxBytes, tempDir, useScale: false, 0, cancellationToken)
                .ConfigureAwait(false);
            if (pass1.Success)
                return (true, pass1.Bytes, "video.mp4", "video/mp4", null);

            foreach (var h in DownscaleHeights)
            {
                var pass = await TryRunVariantsAsync(inputPath, maxBytes, tempDir, useScale: true, h, cancellationToken)
                    .ConfigureAwait(false);
                if (pass.Success)
                    return (true, pass.Bytes, "video.mp4", "video/mp4", null);
            }

            return (false, null, null, null,
                $"Не удалось сжать видео до {(maxBytes + 1048576 - 1) / 1048576} МБ.");
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
                // ignore temp cleanup failures
            }
        }
    }

    private static async Task<(bool Success, byte[]? Bytes)> TryRunVariantsAsync(string inputPath, int maxBytes, string tempDir,
        bool useScale, int height, CancellationToken cancellationToken)
    {
        foreach (var qp in CompressionQpSteps)
        {
            var outPath = Path.Combine(tempDir, $"out_{(useScale ? height.ToString(CultureInfo.InvariantCulture) : "src")}_{qp}.mp4");
            var vf = useScale ? " -vf \"scale=-2:" + height.ToString(CultureInfo.InvariantCulture) + ":force_original_aspect_ratio=decrease\"" : "";
            var args =
                $"-y -i \"{inputPath}\" -c:v libopenh264 -qp {qp}{vf} -c:a aac -b:a 96k -movflags +faststart \"{outPath}\"";
            var (exitCode, _, _) = await RunToolAsync(FfmpegExePath, args, cancellationToken).ConfigureAwait(false);
            if (exitCode != 0 || !File.Exists(outPath))
                continue;

            var fi = new FileInfo(outPath);
            if (fi.Length <= 0 || fi.Length > maxBytes)
                continue;

            var bytes = await File.ReadAllBytesAsync(outPath, cancellationToken).ConfigureAwait(false);
            return (true, bytes);
        }

        return (false, null);
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunToolAsync(string fileName, string args,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = new Process { StartInfo = psi };
        process.Start();

        var outTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var stdOut = await outTask.ConfigureAwait(false);
        var stdErr = await errTask.ConfigureAwait(false);
        return (process.ExitCode, stdOut, stdErr);
    }
}
