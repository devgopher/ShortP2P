namespace ShortP2P.Client.Routing;

/// <summary>Параметры поиска по графу и повторов при ошибке отправки (настраиваются через <see cref="P2pRoutingSettingsStore"/>).</summary>
public sealed class P2pRoutingSettings
{
    /// <summary>Максимальная глубина поиска (число рёбер от инициатора), 1–3.</summary>
    public int MaxSearchHops { get; set; } = 3;

    /// <summary>Сколько раз повторить полный поиск при неудаче отправки.</summary>
    public int SendFailureSearchAttempts { get; set; } = 3;

    /// <summary>Пауза между попытками поиска/отправки.</summary>
    public TimeSpan SendFailureRetryDelay { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Ожидание ответа на FIND.</summary>
    public TimeSpan SearchWaitTimeout { get; set; } = TimeSpan.FromSeconds(4);

    /// <summary>Пресет скорости для симуляции канала (data UDP / Bluetooth), не влияет на порт presence.</summary>
    public LinkTechnologyPreset LinkTechnology { get; set; } = LinkTechnologyPreset.Unlimited;
}
