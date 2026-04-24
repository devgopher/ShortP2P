using System.ComponentModel.DataAnnotations;

namespace ShortP2P.Discovery.RouteTables;

public class Route
{
    [Key]
    public required string RouteId { get; set; }
    
    public List<PeerIdentityAddress>? PeerRoutes { get; init; }
}

/// <summary>
/// Конкретный адрес peer
/// </summary>
public class PeerIdentityAddress
{
    public required PeerIdentity PeerIdentity { get; init; }
    public required string PeerAddress { get; init; }
}