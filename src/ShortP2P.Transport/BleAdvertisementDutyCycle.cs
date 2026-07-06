namespace ShortP2P.Transport;

/// <summary>
///     Чередование BLE-рекламы: 5 с transmit, затем 50 с ± jitter только приём чужих ADV.
/// </summary>
public static class BleAdvertisementDutyCycle
{
    public static readonly TimeSpan AdvertiseOnDuration = TimeSpan.FromSeconds(5);

    private const int ListenOnlyBaseMs = 5_000;
    private const int ListenJitterMinMs = 100;
    private const int ListenJitterMaxMs = 1200;

    public static int NextListenOnlyDurationMs() =>
        ListenOnlyBaseMs + Random.Shared.Next(ListenJitterMinMs, ListenJitterMaxMs + 1);
}
