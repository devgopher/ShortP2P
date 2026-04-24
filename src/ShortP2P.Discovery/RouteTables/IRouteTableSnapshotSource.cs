namespace ShortP2P.Discovery.RouteTables;

/// <summary>
///     Снимок маршрутной таблицы для ответа на wire-запрос (узлы с <c>PeerSearch</c> в presence).
/// </summary>
public interface IRouteTableSnapshotSource
{
    ValueTask<IReadOnlyList<Route>> GetRoutesAsync(CancellationToken cancellationToken = default);
}
