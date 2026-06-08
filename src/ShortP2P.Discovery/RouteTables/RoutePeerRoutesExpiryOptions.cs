namespace ShortP2P.Discovery.RouteTables;

/// <summary>
///     Параметры фонового удаления устаревших записей <see cref="PeerIdentityAddress" /> и <see cref="PeerChain" />.
/// </summary>
public sealed class RoutePeerRoutesExpiryOptions
{
    /// <summary>
    ///     Записи старше этого интервала удаляются:
    ///     <see cref="PeerIdentityAddress.LastSeen" /> и <see cref="PeerChain.UpdatedAtUtc" />.
    /// </summary>
    public TimeSpan StaleAfter { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     Пауза между проходами очистки.
    /// </summary>
    public TimeSpan CleanupPeriod { get; set; } = TimeSpan.FromMinutes(1);
}