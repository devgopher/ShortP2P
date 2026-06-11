using Microsoft.EntityFrameworkCore;
using ShortP2P.Auth.Data;
using ShortP2P.Transport.Abstractions;

namespace ShortP2P.Discovery.RouteTables;

public sealed class EfPeerRouteWriter(IDbContextFactory<RouteDbContext> dbFactory) : IPeerRouteWriter
{
    public async Task AddOrUpdatePeerRouteAsync(CompressedNetworkId networkId, string peerAddress, string? nickname,
        TransportKind transportKind, CancellationToken cancellationToken = default)
    {
        if (networkId.IsEmpty || string.IsNullOrWhiteSpace(peerAddress))
            return;

        var routeId = networkId.ToShortString();
        var address = peerAddress.Trim();
        var nick = string.IsNullOrWhiteSpace(nickname) ? "?" : nickname.Trim();

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var route = await db.Routes
            .Include(r => r.PeerRoutes)
            .FirstOrDefaultAsync(r => r.RouteId == routeId, cancellationToken)
            .ConfigureAwait(false);

        if (route == null)
        {
            route = new Route
            {
                RouteId = routeId,
                PeerRoutes = []
            };
            db.Routes.Add(route);
        }

        var identity = new PeerIdentity(nick, networkId);
        var existingPeer = route.PeerRoutes.FirstOrDefault(p =>
            p.PeerIdentity.NetworkId == networkId &&
            string.Equals(p.PeerAddress, address, StringComparison.OrdinalIgnoreCase));
        if (existingPeer == null)
        {
            route.PeerRoutes.Add(new PeerIdentityAddress
            {
                RouteId = routeId,
                PeerIdentity = identity,
                PeerAddress = address,
                LastSeen = DateTime.UtcNow
            });
        }
        else
        {
            existingPeer.LastSeen = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
