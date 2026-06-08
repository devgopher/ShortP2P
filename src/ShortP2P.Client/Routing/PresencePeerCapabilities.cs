namespace ShortP2P.Client.Routing;

/// <summary>
///     Маска ролей узла (устройство + приложение ShortP2P), заявляемая в discovery/presence ping
///     (<see cref="PresencePingCodec" />): 16 бит BE после байта <see cref="LinkTechnologyPreset" />.
///     Продуктовые имена ролей (Messaging, Discovery, …) и таблица соответствия — в README слоя
///     ShortP2P.Discovery, раздел «Узел и возможности». При приёме неизвестные биты отбрасывать через
///     <see cref="AllDefined" />; бит <see cref="Chat" /> (Messaging) кодек всегда принудительно включает при
///     сборке/разборе.
/// </summary>
[Flags]
public enum PresencePeerCapabilities : ushort
{
    None = 0,

    /// <summary>
    ///     Messaging — прямой приём и передача сообщений чата; базовая роль узла, в пинге всегда считается включённой.
    /// </summary>
    Chat = 1 << 0,

    /// <summary>Discovery — участие в поиске/объявлении узлов (discovery-слой, LAN и др.).</summary>
    PeerSearch = 1 << 1,

    /// <summary>Retranslation — ретрансляция трафика для других узлов.</summary>
    Relay = 1 << 2,

    /// <summary>
    ///     Зашифрованное временное хранилище — зарезервировано в wire (бит 3); вне текущего перечня продуктовых ролей
    ///     узла.
    /// </summary>
    EncryptedTemporaryStorage = 1 << 3,

    /// <summary>
    ///     ApplicationHosting — хостинг сторонних приложений и ботов на узле (на будущее). Имя члена enum в коде —
    ///     BotHosting (совместимость).
    /// </summary>
    BotHosting = 1 << 4,

    /// <summary>Все биты, определённые в текущей версии wire-формата (остальные игнорировать при приёме).</summary>
    AllDefined = Chat | PeerSearch | Relay | EncryptedTemporaryStorage | BotHosting
}