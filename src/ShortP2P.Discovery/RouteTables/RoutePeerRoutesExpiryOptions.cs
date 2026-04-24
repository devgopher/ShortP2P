namespace ShortP2P.Discovery.RouteTables;

/// <summary>
///     Параметры фонового удаления устаревших записей <see cref="PeerIdentityAddress" />.
/// </summary>
public sealed class RoutePeerRoutesExpiryOptions
{
    /// <summary>
    ///     Записи старше этого интервала относительно <see cref="PeerIdentityAddress.LastSeen" /> удаляются.
    /// </summary>
    public TimeSpan StaleAfter { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     Пауза между проходами очистки.
    /// </summary>
    public TimeSpan CleanupPeriod { get; set; } = TimeSpan.FromMinutes(1);
}
