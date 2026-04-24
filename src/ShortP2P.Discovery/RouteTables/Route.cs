using System.ComponentModel.DataAnnotations;

namespace ShortP2P.Discovery.RouteTables;

public class Route
{
    [Key]
    public required string RouteId { get; set; }
    
    public required List<PeerIdentityAddress> PeerRoutes { get; init; }
}