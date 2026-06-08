namespace ShortP2P.Client.ChatMedia;

public static class DocumentAttachHelper
{
    public static bool TryGetMimeFromExtension(string filePath, out string mime)
    {
        mime = Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".doc" => "application/msword",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".xls" => "application/vnd.ms-excel",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".ppt" => "application/vnd.ms-powerpoint",
            ".odt" => "application/vnd.oasis.opendocument.text",
            ".ods" => "application/vnd.oasis.opendocument.spreadsheet",
            ".odp" => "application/vnd.oasis.opendocument.presentation",
            ".odg" => "application/vnd.oasis.opendocument.graphics",
            ".rtf" => "application/rtf",
            ".pdf" => "application/pdf",
            _ => ""
        };
        return mime.Length > 0;
    }

    /// <summary>Проверка первых байтов на соответствие типичному формату для MIME.</summary>
    public static bool SniffMatchesMime(ReadOnlySpan<byte> head, string mime)
    {
        var m = mime.Trim().ToLowerInvariant();
        if (m == "application/rtf")
            return head.Length >= 5 && head[0] == (byte)'{' && head[1] == (byte)'\\' && head[2] == (byte)'r'
                   && head[3] == (byte)'t' && head[4] == (byte)'f';

        if (m == "application/pdf")
            return head.Length >= 5 && head[0] == (byte)'%' && head[1] == (byte)'P' && head[2] == (byte)'D'
                   && head[3] == (byte)'F';

        if (m is "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
            or "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            or "application/vnd.openxmlformats-officedocument.presentationml.presentation"
            or "application/vnd.oasis.opendocument.text"
            or "application/vnd.oasis.opendocument.spreadsheet"
            or "application/vnd.oasis.opendocument.presentation"
            or "application/vnd.oasis.opendocument.graphics")
            return head.Length >= 4 && head[0] == (byte)'P' && head[1] == (byte)'K' && head[2] == 0x03
                   && head[3] == 0x04;

        if (head.Length < 8)
            return false;
        if (m is "application/msword" or "application/vnd.ms-excel" or "application/vnd.ms-powerpoint")
            return head[0] == 0xD0 && head[1] == 0xCF && head[2] == 0x11 && head[3] == 0xE0
                   && head[4] == 0xA1 && head[5] == 0xB1 && head[6] == 0x1A && head[7] == 0xE1;

        return false;
    }
}