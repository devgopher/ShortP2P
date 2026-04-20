using Microsoft.Maui.Controls;

namespace ShortP2P.MauiApp;

/// <summary>Строка списка сообщений в чате (текст, изображение или файл).</summary>
public sealed class MessageRowVm
{
    public required string CaptionLine { get; init; }
    public required string TextBody { get; init; }
    /// <summary>Текст вложения с выделением «Скачать» цветом ссылки.</summary>
    public FormattedString? FileBodyFormatted { get; init; }
    public bool ShowTextBody { get; init; }
    public bool IsImage { get; init; }
    /// <summary>Вложение-документ; для сохранения используйте <see cref="MessageId" />.</summary>
    public bool IsFile { get; init; }
    public int MessageId { get; init; }
    public ImageSource? ImagePreview { get; init; }
    public required Color MessageColor { get; init; }
    public bool ShowDelivery { get; init; }
    public required string DeliveryGlyph { get; init; }
    public required Color DeliveryGlyphColor { get; init; }
}
