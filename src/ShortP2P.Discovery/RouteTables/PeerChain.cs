using System.ComponentModel.DataAnnotations;
using ShortP2P.Auth.Data;

namespace ShortP2P.Discovery.RouteTables;

/// <summary>
///     Цепочка узлов от исходного маршрута до целевого узла.
/// </summary>
public sealed class PeerChain
{
    [Key] public long Id { get; set; }

    /// <summary>
    ///     RouteId узла, от которого построена цепочка.
    /// </summary>
    public required string SourceRouteId { get; set; }

    /// <summary>
    ///     Сетевой id конечного узла цепочки.
    /// </summary>
    public CompressedNetworkId TargetNetworkId { get; set; }

    /// <summary>
    ///     Сигнатура цепочки для дедупликации.
    /// </summary>
    public required string ChainKey { get; set; }

    /// <summary>
    ///     Время актуализации цепочки (UTC).
    /// </summary>
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    ///     Последовательный набор адресов в цепочке.
    /// </summary>
    public required List<PeerIdentityAddress> PeerChainNodes { get; set; }
}