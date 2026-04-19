using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShortP2P.Client.ChatMedia;

/// <summary>Лимиты вложений в чат. JSON-файл по умолчанию рядом с приложением: <c>chat-media.json</c>.</summary>
public sealed class ChatMediaOptions
{
    private static readonly JsonSerializerOptions JsonRead = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Максимальный размер одного изображения (байт), по умолчанию 100 КиБ.</summary>
    public int MaxImageBytes { get; set; } = 100 * 1024;

    /// <summary>Разрешённые MIME-типы изображений.</summary>
    public List<string> AllowedImageMimeTypes { get; set; } =
    [
        "image/jpeg",
        "image/png",
        "image/gif",
    ];

    /// <summary>Максимальный размер одного документа (Word / LibreOffice и т.п.), по умолчанию 10 МиБ.</summary>
    public int MaxDocumentBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>Верхняя граница размера расшифрованного бинарного кадра чата (вложение + заголовок wire).</summary>
    public int MaxMessengerBinaryBytes => MaxDocumentBytes + 256 * 1024;

    /// <summary>Разрешённые MIME для вложений-документов.</summary>
    public List<string> AllowedDocumentMimeTypes { get; set; } =
    [
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "application/vnd.ms-powerpoint",
        "application/vnd.oasis.opendocument.text",
        "application/vnd.oasis.opendocument.spreadsheet",
        "application/vnd.oasis.opendocument.presentation",
        "application/vnd.oasis.opendocument.graphics",
        "application/rtf",
        "application/pdf",
    ];

    public static ChatMediaOptions LoadOrDefault(string? jsonPath)
    {
        var o = new ChatMediaOptions();
        if (string.IsNullOrWhiteSpace(jsonPath) || !File.Exists(jsonPath))
            return o;

        try
        {
            var json = File.ReadAllText(jsonPath);
            var dto = JsonSerializer.Deserialize<ChatMediaFileDto>(json, JsonRead);
            if (dto == null)
                return o;
            if (dto.MaxImageBytes is >= 4096 and <= 10 * 1024 * 1024)
                o.MaxImageBytes = dto.MaxImageBytes.Value;
            if (dto.MaxDocumentBytes is >= 256 * 1024 and <= 15 * 1024 * 1024)
                o.MaxDocumentBytes = dto.MaxDocumentBytes.Value;
            if (dto.AllowedImageMimeTypes is { Count: > 0 } list)
                o.AllowedImageMimeTypes = list
                    .Select(s => s.Trim().ToLowerInvariant())
                    .Where(s => s.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            if (dto.AllowedDocumentMimeTypes is { Count: > 0 } docList)
                o.AllowedDocumentMimeTypes = docList
                    .Select(s => s.Trim().ToLowerInvariant())
                    .Where(s => s.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
        }
        catch
        {
            // ignore bad config
        }

        return o;
    }

    public void ValidateMime(string mimeType)
    {
        var m = mimeType.Trim().ToLowerInvariant();
        if (!AllowedImageMimeTypes.Any(a => string.Equals(a, m, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"Unsupported image type: {mimeType}", nameof(mimeType));
    }

    public void ValidateSize(int byteLength)
    {
        if (byteLength <= 0)
            throw new ArgumentException("Image is empty.", nameof(byteLength));
        if (byteLength > MaxImageBytes)
            throw new ArgumentException($"Image exceeds limit ({MaxImageBytes} bytes).", nameof(byteLength));
    }

    public void ValidateDocumentMime(string mimeType)
    {
        var m = mimeType.Trim().ToLowerInvariant();
        if (!AllowedDocumentMimeTypes.Any(a => string.Equals(a, m, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"Unsupported document type: {mimeType}", nameof(mimeType));
    }

    public void ValidateDocumentSize(int byteLength)
    {
        if (byteLength <= 0)
            throw new ArgumentException("Document is empty.", nameof(byteLength));
        if (byteLength > MaxDocumentBytes)
            throw new ArgumentException($"Document exceeds limit ({MaxDocumentBytes} bytes).", nameof(byteLength));
    }

    private sealed class ChatMediaFileDto
    {
        [JsonPropertyName("maxImageBytes")]
        public int? MaxImageBytes { get; set; }

        [JsonPropertyName("maxDocumentBytes")]
        public int? MaxDocumentBytes { get; set; }

        [JsonPropertyName("allowedImageMimeTypes")]
        public List<string>? AllowedImageMimeTypes { get; set; }

        [JsonPropertyName("allowedDocumentMimeTypes")]
        public List<string>? AllowedDocumentMimeTypes { get; set; }
    }
}
