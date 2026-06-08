using System.ComponentModel.DataAnnotations;

namespace ShortP2P.Discovery.RouteTables;

/// <summary>
///     Конкретный адрес peer
/// </summary>
public class PeerIdentityAddress
{
    /// <summary>
    ///     Primary key
    /// </summary>
    [Key]
    public long Id { get; set; }

    /// <summary>
    ///     Id маршрута
    /// </summary>
    public required string RouteId { get; set; }

    /// <summary>
    ///     Id пира
    /// </summary>
    public required PeerIdentity PeerIdentity { get; init; }

    /// <summary>
    ///     Адрес пира (IP/MAC ...)
    /// </summary>
    public required string PeerAddress { get; init; }

    /// <summary>
    ///     Момент последней актуализации адреса (UTC).
    /// </summary>
    public DateTime LastSeen { get; set; }
}