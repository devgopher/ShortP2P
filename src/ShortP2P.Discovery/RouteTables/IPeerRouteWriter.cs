using ShortP2P.Auth.Data;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Discovery.RouteTables;

/// <summary>
///     Добавляет или обновляет запись маршрута для пира по NetworkId.
/// </summary>
public interface IPeerRouteWriter
{
    Task AddOrUpdatePeerRouteAsync(CompressedNetworkId networkId, string peerAddress, string? nickname,
        TransportKind transportKind, CancellationToken cancellationToken = default);
}
