namespace ShortP2P.Client.Services.MessengerServers;

/// <summary>Агрегированный статус связи с messenger-серверами для шапки UI.</summary>
public enum MessengerServerLinkStatus
{
    /// <summary>Серверы не добавлены или все выключены (серый).</summary>
    Disabled = 0,

    /// <summary>Есть активные серверы, идёт подключение / ещё не было ошибок (жёлтый).</summary>
    Waiting = 1,

    /// <summary>Есть рабочая сессия хотя бы с одним сервером (зелёный).</summary>
    Connected = 2,

    /// <summary>Активные серверы есть, но соединения нет (красный).</summary>
    Disconnected = 3
}
