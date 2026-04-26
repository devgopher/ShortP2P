using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ShortP2P.Discovery.RouteTables;

public class Route
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public required string RouteId { get; set; }
    
    public required List<PeerIdentityAddress> PeerRoutes { get; init; }
}