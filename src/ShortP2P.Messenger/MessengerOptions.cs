namespace ShortP2P.Messenger;

/// <summary>
///     Параметры бэкенда мессенджера. Лимит бинарного сообщения можно менять без смены транспорта.
/// </summary>
public sealed class MessengerOptions
{
    /// <summary>
    ///     Максимальный размер расшифрованного бинарного сообщения (байт).
    /// </summary>
    public int MaxBinaryMessageBytes { get; set; } = 1048576;
}