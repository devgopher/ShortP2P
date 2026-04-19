namespace ShortP2P.Client.ChatMedia;

public static class ImageAttachHelper
{
    public static bool TryGetMimeFromExtension(string filePath, out string mime)
    {
        mime = Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            _ => "",
        };
        return mime.Length > 0;
    }

    /// <summary>Проверка сигнатуры файла (первые байты) на соответствие расширению.</summary>
    public static bool SniffMatchesMime(ReadOnlySpan<byte> head, string mime)
    {
        if (head.Length < 4)
            return false;
        return mime switch
        {
            "image/jpeg" => head[0] == 0xFF && head[1] == 0xD8,
            "image/png" => head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47,
            "image/gif" => head[0] == (byte)'G' && head[1] == (byte)'I' && head[2] == (byte)'F' && head[3] == (byte)'8',
            _ => false,
        };
    }
}
