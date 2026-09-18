namespace ShortP2P.Client;

/// <summary>Единые символы статуса доставки исходящих сообщений для UI (MAUI, WinForms).</summary>
public static class OutgoingDeliveryIndicators
{
    /// <summary>Принято сервером / ушло в канал — одна галочка.</summary>
    public const string Sent = "\u2713";

    /// <summary>Доставлено собеседнику (квитанция) — две галочки.</summary>
    public const string Delivered = "\u2713\u2713";

    public const string Pending = "\u23f3";
    public const string Failed = "!";
}