using Microsoft.EntityFrameworkCore;

namespace ShortP2P.Discovery.RouteTables;

public sealed class EfRouteTableSnapshotSource(IDbContextFactory<RouteDbContext> factory) : IRouteTableSnapshotSource
{
    public async ValueTask<IReadOnlyList<Route>> GetRoutesAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Routes
            .AsNoTracking()
            .Include(r => r.PeerRoutes)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
