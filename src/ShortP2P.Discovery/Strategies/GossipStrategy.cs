using Microsoft.EntityFrameworkCore;
using ShortP2P.Discovery.RouteTables;

namespace ShortP2P.Discovery.Strategies;

/// <summary>
///     <see cref="FindAsync" /> — маршрут из локальной БД. <see cref="UpdateRoutesAsync" /> — не реализован.
/// </summary>
public sealed class GossipStrategy(IDbContextFactory<RouteDbContext> dbContextFactory) : IDiscoveryStrategy
{
    private const int MaxPeerChainDepth = 5;
    private readonly IDbContextFactory<RouteDbContext> _dbContextFactory =
        dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));

    public string Name => "gossip";

    public Task<Route[]> UpdateRoutesAsync(int deepness = 3, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("LookupAsync будет возвращать маршруты исключительно из локальной базы; реализация позже.");
    }

    public async Task<PeerChain[]> FindAsync(CompressedNetworkId networkId, int deepness = 5,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var cappedDepth = Math.Clamp(deepness, 1, MaxPeerChainDepth);

        var chains = await db.PeerChains
            .AsNoTracking()
            .Include(c => c.PeerChainNodes.OrderBy(n => EF.Property<int>(n, "OrderIndex")))
            .OrderByDescending(c => c.UpdatedAtUtc)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        return chains
            .Select(chain => TrimByTarget(chain, networkId.Value, cappedDepth))
            .Where(chain => chain != null)
            .Cast<PeerChain>()
            .ToArray();
    }

    private static PeerIdentityAddress Clone(PeerIdentityAddress source) => new()
    {
        RouteId = source.RouteId,
        PeerIdentity = source.PeerIdentity,
        PeerAddress = source.PeerAddress,
        LastSeen = source.LastSeen
    };

    private static PeerChain? TrimByTarget(PeerChain chain, Guid targetNetworkId, int maxDepth)
    {
        if (chain.PeerChainNodes.Count == 0)
            return null;

        var searchLimit = Math.Min(chain.PeerChainNodes.Count, maxDepth);
        var targetIndex = -1;
        for (var i = 0; i < searchLimit; i++)
        {
            if (chain.PeerChainNodes[i].PeerIdentity.NetworkId.Value != targetNetworkId)
                continue;
            targetIndex = i;
            break;
        }

        if (targetIndex < 0)
            return null;

        var trimmedNodes = chain.PeerChainNodes
            .Take(targetIndex + 1)
            .Select(Clone)
            .ToList();
        
        return new PeerChain
        {
            SourceRouteId = chain.SourceRouteId,
            TargetNetworkId = targetNetworkId,
            ChainKey = $"{chain.ChainKey}|to:{targetNetworkId:N}|d:{targetIndex + 1}",
            UpdatedAtUtc = chain.UpdatedAtUtc,
            PeerChainNodes = trimmedNodes
        };
    }
}
