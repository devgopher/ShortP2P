namespace ShortP2P.Client.Routing;

/// <summary>
///     Набор возможностей пира, заявляемых в discovery/presence ping (<see cref="PresencePingCodec" />).
///     Сейчас передаётся как 16-битная битовая маска (BE) после байта <see cref="LinkTechnologyPreset" />; флаги
///     зарезервированы под будущую реализацию — клиент обязан маскировать неизвестные биты через
///     <see cref="AllDefined" />. Семантика каждого бита будет уточняться по мере появления соответствующих
///     подсистем (поиск по сети, ретрансляция, хранилище, боты и т.д.).
/// </summary>
[Flags]
public enum PresencePeerCapabilities : ushort
{
    None = 0,

    /// <summary>P2P-чат ShortP2P. У актуального клиента всегда заявляется в исходящем пинге.</summary>
    Chat = 1 << 0,

    /// <summary>Поиск пиров / участие в LAN-маршрутизации — зарезервировано на будущее.</summary>
    PeerSearch = 1 << 1,

    /// <summary>Ретрансляция трафика для других пиров — зарезервировано на будущее.</summary>
    Relay = 1 << 2,

    /// <summary>Зашифрованное временное хранилище — зарезервировано на будущее.</summary>
    EncryptedTemporaryStorage = 1 << 3,

    /// <summary>Хостинг ботов — зарезервировано на будущее.</summary>
    BotHosting = 1 << 4,

    /// <summary>Все биты, определённые в текущей версии wire-формата (остальные игнорировать при приёме).</summary>
    AllDefined = Chat | PeerSearch | Relay | EncryptedTemporaryStorage | BotHosting,
}
