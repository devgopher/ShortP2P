using ShortP2P.Auth.Data;
using ShortP2P.Discovery.RouteTables;

namespace ShortP2P.Discovery.Strategies;

public interface IDiscoveryStrategy
{
    /// <summary>
    ///     Название стратегии
    /// </summary>
    string Name { get; }

    /// <summary>
    ///     Список маршрутов из локальной базы (без сетевых запросов). Пока не реализовано.
    /// </summary>
    Task<Route[]> UpdateRoutesAsync(int deepness = 3, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Маршрут к пиру из локальной базы по сетевому идентификатору.
    /// </summary>
    Task<PeerChain[]> FindAsync(CompressedNetworkId networkId, int deepness = 5,
        CancellationToken cancellationToken = default);
}
