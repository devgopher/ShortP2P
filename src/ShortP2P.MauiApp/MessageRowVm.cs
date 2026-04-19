namespace ShortP2P.MauiApp;

/// <summary>Строка списка сообщений в чате (текст или изображение).</summary>
public sealed class MessageRowVm
{
    public required string CaptionLine { get; init; }
    public required string TextBody { get; init; }
    public bool ShowTextBody { get; init; }
    public bool IsImage { get; init; }
    public ImageSource? ImagePreview { get; init; }
    public required Color MessageColor { get; init; }
    public bool ShowDelivery { get; init; }
    public required string DeliveryGlyph { get; init; }
    public required Color DeliveryGlyphColor { get; init; }
}
