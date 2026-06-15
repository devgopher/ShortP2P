namespace ShortP2P.Client.Services;

/// <summary>
///     Отправка не удалась; текст сохранён во внутренней очереди и будет отправлен при появлении пира в LAN.
/// </summary>
public sealed class OutboundMessageQueuedException()
    : Exception("Сообщение поставлено в очередь и будет отправлено, когда пир появится в локальной сети.");