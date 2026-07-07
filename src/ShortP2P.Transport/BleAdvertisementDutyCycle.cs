namespace ShortP2P.Transport;

/// <summary>
///     Чередование BLE-рекламы: transmit, затем пауза только приём чужих ADV.
///     При старте — случайный phase offset, чтобы пиры не синхронизировали циклы.
/// </summary>
public static class BleAdvertisementDutyCycle
{
    public static readonly TimeSpan AdvertiseOnDuration = TimeSpan.FromSeconds(8);

    /// <summary>Пауза без своей рекламы: 1000–1100 мс.</summary>
    private const int ListenOnlyBaseMs = 1_000;
    private const int ListenJitterMinMs = 0;
    private const int ListenJitterMaxMs = 100;

    public static int NextListenOnlyDurationMs() =>
        ListenOnlyBaseMs + Random.Shared.Next(ListenJitterMinMs, ListenJitterMaxMs + 1);

    /// <summary>Случайная задержка перед первым циклом (0 … длина полного цикла).</summary>
    public static int NextStartupPhaseOffsetMs()
    {
        var cycleMs = (int)AdvertiseOnDuration.TotalMilliseconds + ListenOnlyBaseMs + ListenJitterMaxMs;
        return Random.Shared.Next(0, cycleMs + 1);
    }
}
