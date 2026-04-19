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
            if (dto.AllowedImageMimeTypes is { Count: > 0 } list)
                o.AllowedImageMimeTypes = list
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

    private sealed class ChatMediaFileDto
    {
        [JsonPropertyName("maxImageBytes")]
        public int? MaxImageBytes { get; set; }

        [JsonPropertyName("allowedImageMimeTypes")]
        public List<string>? AllowedImageMimeTypes { get; set; }
    }
}
