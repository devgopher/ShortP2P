using Microsoft.EntityFrameworkCore;
using ShortP2P.Discovery.RouteTables;

namespace ShortP2P.Discovery.Strategies;

/// <summary>
///     <see cref="FindAsync" /> — маршрут из локальной БД. <see cref="LookupAsync" /> — не реализован.
/// </summary>
public sealed class GossipStrategy(IDbContextFactory<RouteDbContext> dbContextFactory) : IDiscoveryStrategy
{
    private readonly IDbContextFactory<RouteDbContext> _dbContextFactory =
        dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));

    public string Name => "gossip";

    public Task<Route[]> LookupAsync(int deepness = 3, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(
            "LookupAsync будет возвращать маршруты исключительно из локальной базы; реализация позже.");
    }

    public async Task<Route?> FindAsync(CompressedNetworkId networkId, int deepness = 3,
        CancellationToken cancellationToken = default)
    {
        _ = deepness;
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var routeKey = networkId.ToShortString();

        var byRouteId = await db.Routes
            .AsNoTracking()
            .Include(r => r.PeerRoutes)
            .FirstOrDefaultAsync(r => r.RouteId == routeKey, cancellationToken)
            .ConfigureAwait(false);
        if (byRouteId != null)
            return byRouteId;

        return await db.Routes
            .AsNoTracking()
            .Include(r => r.PeerRoutes.OrderByDescending(pr => pr.LastSeen))
            .FirstOrDefaultAsync(r => r.PeerRoutes.Any(p => p.PeerIdentity.NetworkId.Value == networkId.Value),
                cancellationToken)
            .ConfigureAwait(false);
    }
}